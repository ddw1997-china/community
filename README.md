## 📗 FAQ

### 在 Windows 10 以下版本系统中，部分图标显示为 「□」 怎么办？

[点击下载](https://aka.ms/SegoeFonts "SegoeFonts") SegoeFonts 文件，安装压缩包中 `SegMDL2.ttf` 字体后重启即可解决。

### 点击放映后一翻页就闪退

请[激活 Microsoft Office](https://www.coolhub.top/archives/14)。

### 放映后画板程序不会切换到 PPT 模式

>[!note]
> 请尝试将PPT联动架构调整为`ROT`或者`VSTO`，如果这不能解决你的问题请参考以下教程。

1. PowerPoint 处在保护模式下（只读），请退出保护模式，方法如下：
   1. 打开 PowerPoint，点击左上角的「文件」选项；
   2. 在「信息」标签内，点击右侧的「启用编辑」按钮。
2. 曾经安装过 WPS Office 办公软件，导致 COM 组件被破坏，解决方法为完全卸载 WPS Office 后重新安装 Microsoft Office Mondo 2016 即可解决。
3. 请确保 PowerPoint 和本应用运行在同一权限下，如果 PowerPoint 以管理员身份运行而本应用以普通用户身份运行，也会出现无法切换到 PPT 模式的现象，您可以通过检查 PowerPoint 的兼容性设置或提权本应用运行来解决该问题。   
4. 如果上述方法不能解决你的问题，请参考这个链接[【点击此处以跳转】](https://www.inkeys.top/tutorial/ppt-com.html)   

### 程序无法正常启动

请检查你的电脑上是否安装了 `.Net Runtime 6.0` 或更高版本。若没有，请[前往官网](https://dotnet.microsoft.com/zh-cn/download/dotnet/6.0)下载安装。

如果仍无法运行，请[安装 `Microsoft Office`](https://www.coolhub.top/archives/11)。

## 🛠️ 从源码编译

以下步骤产出与本项目发布的手工包**相同的文件结构**（x86、自包含单文件、不裁剪），不要求 SHA256 一致，但保证解压即可完整运行。

### 1. 环境要求

| 依赖 | 说明 |
| --- | --- |
| Windows 10 1809+ / Windows 11 | 仅支持 Windows |
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | 主程序目标框架为 `net10.0-windows10.0.19041.0`，需含 x86 目标包 |
| Visual Studio 2022 或 Build Tools | 勾选「.NET 桌面开发」与「Office/SharePoint 开发」，用于编译 PowerPoint VSTO 加载项 |
| Git | 版本号由 Nerdbank.GitVersioning 从 git 提交计算，**不能只下载源码压缩包** |
| PowerShell 5.1+ | 执行一键脚本 |
| VSTO 4.0 Runtime | 仅在使用 `ppt-agent`（PPT 联动）时需要 |

### 2. 一键构建（推荐）

```powershell
git clone https://github.com/ddw1997-china/community.git
cd community
powershell -ExecutionPolicy Bypass -File .\build-release.ps1 -Deterministic -SkipSmokeTest
```

产物为 `artifacts\InkCanvasForClass.CE.<版本>.zip`，解压后即可运行；脚本会依次完成加载项构建、锁定还原、发布、清理运行期文件、打包并打印 SHA256。

### 3. 手工构建

**3.1 还原依赖（锁定模式，保证包版本与仓库一致）**

```powershell
dotnet restore "Ink Canvas/InkCanvasForClass.csproj" --locked-mode -p:Platform=x86
```

**3.2 编译 PowerPoint 加载项（`ppt-agent` 的来源，必须在前）**

publish 只会把 `InkCanvas.PowerPointAddIn\bin\Release\` 里已存在的文件复制进 `ppt-agent`，不会自动编译它。

```powershell
msbuild "InkCanvas.PowerPointAddIn/InkCanvas.PowerPointAddIn.csproj" `
  /p:Configuration=Release `
  /p:SignManifests=false `
  /verbosity:minimal -maxcpucount
```

没有代码签名证书时用 `/p:SignManifests=false`；有证书时改为 `/p:SignManifests=true /p:ManifestCertificateThumbprint=<证书指纹>`。

**3.3 发布主程序**

```powershell
dotnet publish "Ink Canvas/InkCanvasForClass.csproj" `
  --configuration Release `
  --no-restore `
  --runtime win-x86 `
  --self-contained true `
  --output "artifacts/publish/x86-clean2" `
  -p:Platform=x86 `
  -p:PublishSingleFile=true `
  -p:PublishTrimmed=false `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:UseMultiToolTask=true `
  -p:EnforceProcessCountAcrossBuilds=true `
  -p:RunAnalyzers=false
```

关键参数不能改：`Platform=x86` + `win-x86` 决定架构，`PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract` 生成带原生库的单文件，`PublishTrimmed=false` 避免 WPF 反射被裁剪。

### 4. 校验与清理

发布目录应为 **16 个文件**：

```
InkCanvasForClass.exe
InkCanvasForClass.dll.config
InkCanvas.Controls.xml
InkCanvas.PluginSdk.xml
InkCanvas.IACoreHelper.exe
InkCanvas.IACoreHelper.exe.config
ppt-agent\InkCanvas.PowerPointAddIn.dll
ppt-agent\InkCanvas.PowerPointAddIn.dll.manifest
ppt-agent\InkCanvas.PowerPointAddIn.vsto
ppt-agent\InkCanvas.PPTAgent.Contracts.dll
ppt-agent\Microsoft.Office.Tools.Common.dll
ppt-agent\Microsoft.Office.Tools.Common.v4.0.Utilities.dll
ppt-agent\Microsoft.Office.Tools.dll
ppt-agent\Microsoft.Office.Tools.v4.0.Framework.dll
ppt-agent\Microsoft.VisualStudio.Tools.Applications.Runtime.dll
ppt-agent\Newtonsoft.Json.dll
```

- 版本号：`FileVersion` 等于 [version.json](version.json) 中的 `version`，`ProductVersion` 末尾附带当前提交的短 SHA。
- 试运行一次后，需要从发布目录删除运行期产生的 `Configs`、`Logs`、`Saves`、`Plugins`、`PluginConfigs`、`PluginLogs`、`PluginPackages`、`Backups`、`Automations`、`Crashes`、`device_id.dat`、`usage_stats.enc`，以及首次启动解出的 `IACore.dll`、`IALoader.dll`、`IAWinFX.dll`，否则发布包会夹带用户数据。
- 引用项目被重新编译时会在发布目录带出 `*.pdb`，发布前一并删除。

### 5. 常见问题

- **`ppt-agent` 为空或缺少 `Microsoft.Office.Tools.Common.v4.0.Utilities.dll`**：加载项没有先编译，回到 3.2 步。
- **还原报 `NU1004`**：`packages.lock.json` 与 csproj 不一致。确认是预期的依赖变更后执行 `dotnet restore --force-evaluate` 并提交新的锁文件，日常构建不要去掉 `--locked-mode`。
- **产物体积异常（如单文件超过 240 MB）**：`bin`/`obj` 里混入了其它平台或其它配置的中间产物，删除这些目录后重新发布。
- **换目录/换机器构建**：保持提交一致并用 `-Deterministic`（内部启用 `ContinuousIntegrationBuild` 与 `PathMap`），可让不同路径下产出的文件内容一致。

