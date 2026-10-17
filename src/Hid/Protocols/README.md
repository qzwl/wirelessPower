# 新增 2.4G HID 协议适配器

本文说明如何为 `BatteryTray` 增加一个新的 USB HID 电量协议适配器。

> 这里只涉及 **USB HID / 2.4G 接收器**。蓝牙电量走 `BluetoothBatteryReader`，不实现 `IProtocolAdapter`，新增蓝牙协议需要单独修改蓝牙读取逻辑。

## 目录

- [整体流程](#整体流程)
- [先确认设备接口](#先确认设备接口)
- [适配器契约](#适配器契约)
- [最小实现模板](#最小实现模板)
- [常用 HID API](#常用-hid-api)
- [匹配与优先级](#匹配与优先级)
- [返回值约定](#返回值约定)
- [超时、休眠与错误处理](#超时休眠与错误处理)
- [注册适配器](#注册适配器)
- [测试与提交](#测试与提交)
- [检查清单](#检查清单)

## 整体流程

```text
HidEnumerator 枚举 HID 接口
        │
        ▼
按 GroupKey 合并成 DeviceGroup
        │
        ▼
按 Priority 依次调用 Matches()
        │
        ▼
TryRead() 返回 PeripheralReading
        │
        ▼
BatteryMonitor 取第一个成功读数
```

新增一个适配器通常只需要：

1. 用 `--probe` 找到设备的 VID/PID、Usage Page、Usage 和报告长度。
2. 在 `src/Hid/Protocols/` 新建 `XxxAdapter.cs`，实现 `IProtocolAdapter`。
3. 在 `BatteryMonitor` 的 `_adapters` 列表中注册。
4. 用 `--probe` 验证读数、充电状态和设备名。
5. 在根目录 README 的“支持的设备”表中补充说明。

## 先确认设备接口

先运行：

```powershell
dist\2.4G-Battery-Tray.exe --probe result.json
```

重点看 JSON 中的：

- `VendorId` / `ProductId`（VID:PID）
- `UsagePage` / `Usage`
- `InputReportLength` / `OutputReportLength` / `FeatureReportLength`
- `Product` / `Manufacturer`
- `Path`（HID 接口路径）
- `Caps` 是否存在
- `Error`（接口是否无法打开）

一个物理接收器经常暴露多个 HID 接口：键盘、鼠标、Consumer Control、厂商私有接口等。它们会通过 `GroupKey` 归入同一个 `DeviceGroup`，所以适配器应该遍历 `group.Interfaces`，用 **Usage Page + Usage + 报告长度** 精确选择接口，而不是假设第一个接口就是协议接口。

### 厂商私有接口的典型特征

- `UsagePage >= 0xFF00`（`HidInterface.IsVendorPage`）
- 有 `OutputReportLength` 或 `FeatureReportLength`
- Usage 可能是 `0x0001`、`0x0002`、`0x000E`、`0x0092` 等厂商自定义值

私有协议的 Usage/Report 长度没有统一标准，必须结合设备实际描述符和抓包确定。

## 适配器契约

接口定义在 `IProtocolAdapter.cs`：

```csharp
internal interface IProtocolAdapter
{
    string Name { get; }
    int Priority { get; }
    bool Matches(DeviceGroup group);
    PeripheralReading? TryRead(DeviceGroup group);
}
```

### `Name`

显示在详情窗口的“数据来源”中，建议写清楚协议或厂商，例如：

```csharp
public string Name => "ATK / VXE EVISION";
```

### `Priority`

数字越小越先执行。当前约定：

| 优先级 | 类型 | 示例 |
|---:|---|---|
| 10–39 | 设备专用私有协议 | ATK/VXE EVISION、Rapoo、DARKRIM |
| 40–79 | 厂商 Feature Report / 通用厂商协议 | ATK 鼠标、Endgame、VAXEE、Nordic、Logitech HID++ |
| 90+ | 标准 HID 电量字段 | Battery Strength / State of Charge |

新增适配器时，选择一个能体现“专用协议优先于通用协议”的数值即可，不要求和现有值连续。

### `Matches`

只做**快速、无 I/O** 的判断。可以检查：

- `HidInterface.VendorId` / `ProductId`
- `UsagePage` / `Usage`
- `InputReportLength` / `OutputReportLength` / `FeatureReportLength`
- 是否存在可用的 HID 接口

不要在 `Matches` 中打开设备、读写报告或做耗时操作；它会对每个设备组、每个适配器调用。

匹配应尽量严格：

```csharp
public bool Matches(DeviceGroup group)
{
    return group.Interfaces.Any(i =>
        i.VendorId == 0x1234 &&
        i.UsagePage == 0xFF00 &&
        i.Usage == 0x0002 &&
        i.OutputReportLength >= 9);
}
```

如果同厂商有多个协议，务必用 Usage Page / Usage / 报告长度区分，避免抢占其他设备的读取。

### `TryRead`

真正打开 HID 接口并读取电量。返回 `PeripheralReading`，或返回 `null` 表示“本适配器不适用/无结果”。

不要缓存上一次的 `HidDevice`；每次读取重新 `Open`，用 `using` 释放。

## 最小实现模板

下面是一个虚构的 `ExampleAdapter`，演示 Output Report + Input Report 的典型流程：

```csharp
using System.Diagnostics;

namespace BatteryTray.Hid.Protocols;

internal sealed class ExampleAdapter : IProtocolAdapter
{
    public string Name => "Example 2.4G";
    public int Priority => 45;

    public bool Matches(DeviceGroup group)
    {
        return group.Interfaces.Any(i =>
            i.VendorId == 0x1234 &&
            i.UsagePage == 0xFF00 &&
            i.Usage == 0x0002 &&
            i.OutputReportLength >= 9);
    }

    public PeripheralReading? TryRead(DeviceGroup group)
    {
        var iface = group.Interfaces.FirstOrDefault(i =>
            i.VendorId == 0x1234 &&
            i.UsagePage == 0xFF00 &&
            i.Usage == 0x0002 &&
            i.OutputReportLength >= 9);

        if (iface is null)
        {
            return null;
        }

        try
        {
            using var device = HidDevice.Open(iface.Path, write: true);
            if (!device.CanWrite)
            {
                return ProtocolHelper.Failure(
                    group, "HID 接口只读，无法发送查询", PeripheralKind.Mouse, "Example 私有协议");
            }

            device.Drain();

            // Report ID 4：查询电量
            byte[] request = ProtocolHelper.Report(4, iface.OutputReportLength, r =>
            {
                r[1] = 0x01;
                r[2] = 0x00;
                r[3] = 0x1A;
            });

            // 某些接收器必须用 WriteFile；不要让 HidD_SetOutputReport 静默吞掉命令
            device.WriteOutput(request, allowHidDeeFallback: false);

            var timeout = Stopwatch.StartNew();
            while (timeout.ElapsedMilliseconds < 900)
            {
                byte[]? response = device.ReadInput(250);
                if (response is null || response.Length < 9)
                {
                    continue;
                }

                // 响应第 0 字节是 Report ID，第 3 字节是命令
                if (response[0] != 4 || response[3] != 0x1A)
                {
                    continue;
                }

                int level = response[8];
                if (!ProtocolHelper.IsValidLevel(level))
                {
                    continue;
                }

                bool? charging = response.Length > 9 ? response[9] != 0 : null;
                return ProtocolHelper.Success(
                    group,
                    level,
                    charging,
                    "Example 私有协议",
                    PeripheralKind.Mouse);
            }

            return ProtocolHelper.Failure(
                group,
                "设备未响应（移动鼠标唤醒后重试）",
                PeripheralKind.Mouse,
                "Example 私有协议");
        }
        catch (Exception ex)
        {
            return ProtocolHelper.Failure(
                group,
                $"读取失败：{ex.Message}",
                PeripheralKind.Mouse,
                "Example 私有协议");
        }
    }
}
```

## 常用 HID API

### 打开接口

```csharp
using var device = HidDevice.Open(iface.Path, write: true);
```

- `write: true` 尝试读写；如果接口只允许读，`HidDevice` 会回退为只读句柄，`device.CanWrite == false`。
- 需要写入协议的适配器应先检查 `device.CanWrite`，否则 `WriteOutput` 会抛 `IOException`。
- 只读协议（Feature Report）用 `write: false`。
- 必须先检查 `iface.Caps` / 报告长度，避免打开明显不匹配的接口。

### 读取 Input Report

```csharp
byte[]? data = device.ReadInput(250);
```

- 返回 `null` 表示超时或没有数据。
- 返回数组包含 Report ID（第 0 字节）。
- 内置 overlapped 读 + 超时取消，不要在调用方再阻塞等待。

### 发送 Output Report

```csharp
device.WriteOutput(report, allowHidDeeFallback: false);
```

- `report` 长度应与 `OutputReportLength` 一致，第 0 字节是 Report ID。
- 优先 `WriteFile`。
- `allowHidDeeFallback: false` 用于**不能依赖 `HidD_SetOutputReport`** 的接收器；部分设备上该 API 返回成功但实际没有发送。
- 写失败会抛 `IOException`，在适配器内捕获并返回失败读数。

### 读取 Feature Report

```csharp
byte[] data = device.GetFeature(reportId, iface.FeatureReportLength);
device.SetFeature(report);
```

### 读取标准 HID 电量字段

标准字段适配器 `StandardBatteryAdapter` 使用：

```csharp
var caps = device.GetValueCaps(HidNative.HIDP_FEATURE);
uint? raw = device.GetUsageValue(HidNative.HIDP_FEATURE, cap, report);
```

当前识别的 Usage：

- `(0x0006, 0x0020)` Battery Strength
- `(0x000D, 0x003B)` Battery Strength（digitizer）
- `(0x0085, 0x0001)` Absolute State of Charge
- `(0x0085, 0x0066)` Relative State of Charge
- `(0x0085, 0x0065)` Battery Level

### 辅助方法

```csharp
ProtocolHelper.Report(reportId, totalLength, fill);
ProtocolHelper.Success(group, level, charging, source, kind, name);
ProtocolHelper.Failure(group, error, kind, source);
ProtocolHelper.IsValidLevel(level);
```

`Success` 会把电量限制在 `0..100`；仍建议在解析时先检查原始值，避免把错误字节当作电量。

## 匹配与优先级

`BatteryMonitor` 会这样处理一个设备组：

1. 找出所有 `Matches(group) == true` 的适配器；
2. 按 `Priority` 升序排序；
3. 依次调用 `TryRead`；
4. 第一个返回 `Level != null` 的读数被采用；
5. 如果都失败，使用第一个返回的失败读数显示错误。

因此：

- 专用协议放在通用协议前面；
- 专用协议匹配失败时，后面的标准 HID 适配器仍可兜底；
- 不要让 `Matches` 返回 true 但 `TryRead` 永远返回 `null`，否则会无谓增加读取时间；
- 如果 `TryRead` 抛异常且未捕获，整个 `BatteryMonitor.Read()` 会失败，所有设备都读不到。

## 返回值约定

### 成功

```csharp
return ProtocolHelper.Success(
    group,
    level,                    // 0..100
    charging,                 // true/false/null
    "协议名称",               // 显示在详情窗口
    PeripheralKind.Mouse);    // Keyboard / Mouse / Headset / Other
```

### 失败但已匹配

```csharp
return ProtocolHelper.Failure(
    group,
    "设备未响应",             // 会显示在详情窗口
    PeripheralKind.Mouse,
    "协议名称");
```

失败读数不会被当作 `Online`，但会作为“发现设备但未读取”显示；如果历史缓存存在，`ReadingCache` 可能补成“上次读数”。

### 不适用

```csharp
return null;
```

只有确定该设备组不属于本协议时返回 `null`；不要用 `null` 表示“设备休眠”。

### 命名

默认使用 `group.FriendlyName`。如果设备名是通用 HID 名称，可以在 `Success` 的 `name` 参数中传入更可读的名字：

```csharp
ProtocolHelper.Success(group, level, charging, "厂商协议", kind, "厂商 型号");
```

## 超时、休眠与错误处理

- 读取超时建议总时长控制在 **约 1 秒**，单次 `ReadInput` 使用 100–300 ms。
- 发送查询前可以调用 `device.Drain()` 清掉旧状态包。
- 设备深度休眠时可能不响应；返回失败读数并在错误中点明“移动/按键唤醒后重试”。
- 不要无限等待，不要使用阻塞式 `ReadFile`。
- 捕获所有 `IOException` / `TimeoutException` / 解析异常，返回 `Failure`，不要让异常冒泡到 `BatteryMonitor`。
- 不要修改设备配置、宏、DPI 或固件；适配器只做只读查询。
- 解析时始终检查数组长度，不要假设响应一定达到 `OutputReportLength`。

## 注册适配器

在 `src/Hid/BatteryMonitor.cs` 的 `_adapters` 中加入新实例：

```csharp
private readonly IReadOnlyList<IProtocolAdapter> _adapters =
[
    new AtkEvisionAdapter(),
    new RapooAdapter(),
    new DarkrimAdapter(),
    new ExampleAdapter(),      // 新增
    new AtkVendorAdapter(),
    new EndgameAdapter(),
    new VaxeeAdapter(),
    new NordicAdapter(),
    new LogitechAdapter(),
    new StandardBatteryAdapter()
];
```

顺序应与 `Priority` 一致。虽然 `BatteryMonitor` 会按 `Priority` 重新排序，但保持列表可读性更好。

如果新适配器只支持一个 VID/PID，可以把它放在更通用的同厂商适配器前面。

## 测试与提交

### 构建

```powershell
dotnet build BatteryTray.csproj -c Debug
```

### 读取一次并检查 JSON

```powershell
dotnet run --project BatteryTray.csproj -- --probe result.json
```

或使用发布版：

```powershell
dist\2.4G-Battery-Tray.exe --probe result.json
```

检查：

- 设备是否出现在 `devices`；
- `Level` 是否是合理值；
- `Charging` 是否正确；
- `Source` 是否显示新协议名；
- 是否误匹配了其他设备。

### 验证失败路径

- 关闭设备/拔掉接收器后再读；
- 让设备休眠后再读；
- 确认不会卡住超过约 1 秒；
- 确认不会影响其他设备读取。

### 提交 PR 时请附上

- 设备型号、VID:PID、接收器/固件版本；
- `--probe` JSON（可删除个人设备名）；
- 能说明请求/响应格式的十六进制数据；
- 实测电量与充电状态；
- 是否与其他设备共用 VID/PID。

## 检查清单

- [ ] `Matches` 精确到 Usage Page / Usage / 报告长度，不只匹配 VID/PID
- [ ] `TryRead` 内部捕获所有异常，返回 `Failure` 或 `null`
- [ ] 解析前检查响应长度
- [ ] 电量通过 `ProtocolHelper.IsValidLevel` 或 `0..100` 检查
- [ ] 单次读取总超时约 1 秒，不无限阻塞
- [ ] 使用 `using` 释放 `HidDevice`
- [ ] 需要写入时明确 `allowHidDeeFallback` 的取值
- [ ] 在 `BatteryMonitor` 注册并按优先级放置
- [ ] 用 `--probe` 验证成功/失败/休眠路径
- [ ] 更新根目录 README 的“支持的设备”表
