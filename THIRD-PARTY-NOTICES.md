# 第三方许可证与来源声明

本项目（2.4G 无线外设电量托盘，仓库 `qzwl/wirelessPower`）是在下列开源项目基础上
派生、修改和扩展而来。以下原始版权声明与许可文本予以完整保留。

---

## 1. 上游原始作品：MrZQAQ/wirelessPower

- 仓库地址：<https://github.com/MrZQAQ/wirelessPower>
- 许可证：MIT License
- 原始版权：Copyright (c) 2026

以下为上游 LICENSE 原文（保持不变）：

```text
MIT License

Copyright (c) 2026

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

Vendor HID protocols used here were derived from the referenced open-source
projects and public vendor driver behavior for interoperability. They are not
affiliated with or endorsed by the device manufacturers.
```

MIT 许可证允许再许可（sublicense），因此本衍生版本可以按 CC BY-NC-SA 4.0
分发，同时按 MIT 要求保留上述版权声明与许可文本。

---

## 2. 协议实现参考的开源项目

以下项目提供了私有 HID/2.4G 协议的实现思路与数据结构参考，
其各自版权归原作者所有，适用各自仓库中的许可证：

- `yszyszysz233/2.4G-battery-viewer` —— 协议与整体思路参考
- `Nuitfanee/ClickSync` —— Rapoo（雷柏）协议参考
- ATK / VXE EVISION —— 协议参考自 ATK HUB Web 版前端实现

---

## 3. 品牌标识（Logo）

`Assets/brands/` 下的品牌标识用于**标识设备制造商**，属于识别性使用（nominative use）。

- 各商标及标识的著作权、商标权归对应品牌方所有；
- 本项目与这些厂商**没有任何关联**，也未获得其官方赞助或授权；
- 标识来源为各品牌官方网站公开的媒体资源，或开源图标库 `simple-icons`（CC0）；
- 这些图片仅用于在界面中指示设备所属品牌，不用于暗示品牌方对本项目的认可。

如品牌方认为使用不当，请提 Issue，将在收到通知后移除对应文件。

---

## 4. 运行时依赖（NuGet 包）

| 包 | 版本 | 许可证 |
|---|---|---|
| `Svg` | 3.4.8 | MS-PL |
| `System.Speech` | 9.0.0 | MIT |

上述包仅作为编译/运行依赖引用，未修改其源代码，其许可证随包分发。
