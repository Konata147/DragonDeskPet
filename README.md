# DragonDeskPet

[![Build and smoke test](https://github.com/Konata147/DragonDeskPet/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/Konata147/DragonDeskPet/actions/workflows/build-and-test.yml)

一只轻量、透明、可拖动的 Windows Q 版龙娘桌宠，也是一个与模型供应商解耦的桌面 AI 助手。

> A lightweight native Windows dragon-girl desktop pet with a provider-neutral AI boundary.

<p align="center">
  <img src="docs/images/desktop-preview.png" width="420" alt="DragonDeskPet 在 Windows 桌面进入睡眠状态" />
</p>

## 当前版本

V0.2 已完成，在完整保留桌宠体验的基础上加入“桌面内容问 AI”：区域截图、用户主动读取的剪贴板文字或图片，以及拖入桌宠的本地图片，都会先预览或填入输入框，确认发送后才会上传给当前模型。

`main` 分支在每次推送和合并请求时通过 GitHub Actions 使用 Windows 与稳定版 .NET 8 自动执行零警告构建和全部冒烟测试。

V0.2 暂不包含非图片文件读取、后台剪贴板监控、提醒、OCR、全局快捷键或截图历史。

## 下载与运行

1. 从 [DragonDeskPet V0.2 Release](https://github.com/Konata147/DragonDeskPet/releases/tag/v0.2.0) 下载 `DragonDeskPet-v0.2.0-win-x64.zip`。
2. 将 ZIP 完整解压到普通文件夹，推荐放在 D 盘等用户选择的位置。
3. 双击 `DragonDeskPet.exe`。

官方 V0.2 ZIP 已自带 .NET 运行环境，适用于 64 位 Windows 10/11，不需要另外安装 .NET。当前版本没有安装程序和数字签名，Windows 首次运行时可能显示来源确认提示。

## 操作方式

| 操作 | 效果 |
| --- | --- |
| 拖动 | 移动桌宠并保存位置 |
| 鼠标滚轮 | 调整桌宠大小 |
| 单击 | 显示或隐藏快捷栏 |
| 双击 | 打开或关闭迷你聊天气泡 |
| 快捷栏“截图”/右键“截图问我” | 选择屏幕区域，预览并确认后交给 AI |
| 快捷栏“粘贴”/右键“剪贴板问我” | 主动读取当前剪贴板文字或图片，不自动发送 |
| 拖入单张图片 | 预览 PNG、JPG、BMP、GIF 或 TIFF，确认后交给 AI |
| 快速连点 | 触发生气状态 |
| 右键 | 打开桌宠菜单 |
| 关闭可见窗口 | 隐藏到系统托盘 |
| 再次启动程序 | 唤醒已有桌宠，不创建第二个常驻实例 |

拖动结束后，桌宠会短暂进入开心状态再恢复待机；即使在角色外、跨屏幕或窗口失焦后松开，也不会一直停留在“被拎起”状态。

## V0.1 功能

- `Idle`、`Hover`、`Dragged`、`Thinking`、`Happy`、`Angry`、`Sleeping` 七种状态。
- 七张独立透明角色素材，单张损坏或缺失时只回退该状态。
- 可切换置顶、持久化位置与 60%–200% 缩放。
- 托盘显示、隐藏、设置和退出。
- 一次性非模态操作引导。
- OpenAI 兼容接口与离线响应，不绑定单一模型供应商。
- Windows 当前用户范围加密保存 API Key。
- 本地崩溃日志、敏感信息脱敏和最近十份保留策略。

## V0.2 功能

- 单显示器内的区域截图，支持多显示器、负坐标与 Per-Monitor V2 DPI。
- 剪贴板文字/图片主动读取，以及单张常见图片拖入桌宠。
- 图片内存预览、可编辑默认问题和明确的发送确认。
- 图片最长边限制为 2560 像素，编码后超过 8 MiB 时拒绝上传。
- OpenAI-compatible 视觉请求；模型不支持图片时给出可重试提示。
- 截图不写入磁盘，发送成功、移除或退出时清除内存数据。

## 本地数据与隐私

设置、日志和运行数据保存在程序旁的 `data/` 文件夹。项目、构建缓存、NuGet 缓存和发布输出都可以留在当前驱动器。

程序不会在用户主动点击发送前向模型服务发送聊天内容、截图、剪贴板内容或拖入图片。程序不监控剪贴板；只有点击“粘贴”或“剪贴板问我”时才读取一次。待发送图片只保存在内存中，不写入配置、缓存、临时目录或日志。API Key 不以明文写入设置文件，崩溃日志不记录聊天文本、请求头、图片数据或完整密钥。

## 从源码构建

需要 Windows 和 .NET 8 SDK：

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\run.ps1
```

脚本会把 .NET CLI 和 NuGet 缓存固定在项目目录中。普通便携版可运行：

```powershell
.\scripts\publish.ps1
```

生成带有 .NET 运行环境的正式 Windows x64 ZIP 和 SHA-256 校验文件：

```powershell
.\scripts\publish-release.ps1 -Version 0.2.0
```

发布脚本默认拒绝覆盖已有的同版本文件。确认目标版本可以重建后，才可显式添加 `-Force`。

## 项目结构

- `src/DragonDeskPet/`：WPF 桌宠程序。
- `tests/DragonDeskPet.SmokeTests/`：状态、素材、设置、单实例、日志和图标测试。
- `assets/character/`：七张正式角色状态素材。
- `assets/icons/`：程序与托盘图标。
- `docs/`：需求、素材规范和验收清单。
- `scripts/`：构建、测试、运行和正式发布脚本。

## 授权

程序源代码采用 [MIT License](LICENSE)。角色美术、程序图标和其他视觉素材不属于 MIT 授权范围，具体规则见 [ASSET_LICENSE.md](ASSET_LICENSE.md)。参考原图不包含在仓库或发布包中。

## 参与贡献和安全

提交代码、文档或素材前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)。如果问题可能涉及密钥、隐私数据、网络传输或本地权限，请不要公开披露细节，并按照 [SECURITY.md](SECURITY.md) 中的方式报告。
