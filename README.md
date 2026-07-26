# Kano 弹幕通知 (danmuku-kano)

把 Windows 系统通知变成弹幕，从屏幕右侧滚到左侧。

QQ、邮件、日历……任何会弹 Toast 通知的应用，消息都会以弹幕形式掠过桌面，全屏游戏和视频时也能看到，不用切出去看通知中心。

## 功能

- **监听全系统通知** —— 基于 `UserNotificationListener`，无需为每个应用单独适配，附带应用图标
- **弹幕样式自定义** —— 字号、速度、透明度、显示区域、密度、字体、颜色、粗体、描边、阴影
- **多屏显示模式** —— 仅主屏 / 所有屏幕（各显示一份）/ 跨屏衔接（一条弹幕连续穿过多个显示器）/ 鼠标所在屏幕
- **性能调度四档** —— 流畅（跟随屏幕刷新率）/ 均衡（60 帧）/ 游戏（30 帧）/ 省电（15 帧）；无弹幕时自动暂停渲染并隐藏覆盖窗口，把独占全屏和独立翻转还给游戏
- **12 种界面语言** —— 简中、繁中、英、日、韩、法、德、西、葡（巴西）、俄、意、土，首次启动跟随系统语言
- **托盘常驻** —— 关闭主界面后继续工作，可选关闭时最小化到托盘或退出、二次确认
- **开机自启** —— 通过 MSIX StartupTask 注册，静默启动（`--autostart`）
- **历史通知** —— 保留最近 100 条，带应用图标

## 运行要求

- Windows 10 1809 (17763) 或更高版本
- 首次启动需在系统弹出的授权框中允许「访问通知」（设置 → 隐私和安全性 → 通知）
- 支持 x64 / x86 / ARM64

## 构建

需要 .NET 8 SDK 和 Visual Studio 2022（含「Windows 应用开发」工作负载）。

```bash
dotnet build danmuku-kano/danmuku-kano.csproj -c Release -p:Platform=x64
```

也可以直接用 Visual Studio 打开 `danmuku-kano.slnx`，选择 x64 配置后生成。

> 通知监听依赖 MSIX 包标识，因此调试和运行都应通过打包方式启动（VS 中直接 F5 即可）；裸跑 exe 时 `UserNotificationListener` 拿不到授权，弹幕不会出现。

## 技术实现

| 层 | 说明 |
| --- | --- |
| 界面 | WinUI 3 (Windows App SDK 1.8)，NavigationView 四个面板：弹幕样式 / 历史通知 / 系统设置 / 关于 |
| 通知源 | WinRT `UserNotificationListener`，抽取应用名、标题、正文、应用图标 |
| 弹幕渲染 | Direct2D + DirectWrite（Vortice 绑定），绘制到每屏一个的分层窗口 |
| 设置存储 | JSON 文件，位于 `%LocalAppData%\KanoDanmaku\settings.json`，300 ms 防抖写入 |

弹幕不走 XAML：渲染器在独立的 STA 线程上，为每个显示器创建一个置顶、点击穿透、无边框的分层窗口（`WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE`），用 Direct2D 画到内存 DC，再通过 `UpdateLayeredWindow` 推上屏。这样弹幕既能盖在全屏应用之上，又不会拦截鼠标。

其他一些实现细节：

- 启动时预热 DirectWrite 字体缓存，避免冷启动后第一条弹幕卡顿数秒
- 弹幕按轨道排布，新弹幕只落在不会追尾的空闲轨道上；轨道排满时排队等待（「允许重叠」密度除外）
- 监听 `WM_DISPLAYCHANGE` / `WM_SETTINGCHANGE`，分辨率变化和显示器热插拔后重建覆盖窗口
- 覆盖窗口每秒重新置顶一次，避免被之后创建的置顶窗口（启动器、录屏、OSD）盖住
- 渲染线程的异常和未处理异常都会记进 `%LocalAppData%\KanoDanmaku\crash.log`

## 目录结构

```
danmuku-kano/
├── App.xaml.cs                 应用入口、全局异常日志、语言初始化
├── MainWindow.xaml(.cs)        设置界面、托盘图标、设置读写
├── Models/
│   └── NotificationItem.cs     通知数据模型（含图标延迟加载）
├── Services/
│   ├── NotificationService.cs      系统通知监听
│   ├── Direct2DDanmakuRenderer.cs  弹幕渲染器
│   ├── DanmakuStyleSettings.cs     样式设置快照
│   ├── SettingsService.cs          JSON 设置存储
│   ├── LocalizationService.cs      多语言切换
│   └── CrashLog.cs                 崩溃日志
└── Strings/                    12 种语言的资源文件
```

## 许可

[MIT](LICENSE.txt)
