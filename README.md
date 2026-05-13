# AI Hub - AI工具管理器

一个使用 WPF 和 Material Design 开发的 AI 工具分类管理桌面应用程序。

## 功能特性

- **分类管理**: 自定义创建、编辑、删除分类
- **应用管理**: 添加、编辑、删除应用程序快捷方式
- **图标显示**: 自动提取可执行文件图标
- **快速启动**: 双击即可启动应用程序
- **搜索功能**: 快速搜索应用
- **主题切换**: 支持明暗主题切换
- **数据持久化**: 自动保存配置到本地

## 技术栈

- .NET 8.0 WPF
- Material Design In XAML Toolkit 5.1.0
- CommunityToolkit.Mvvm 8.2.2
- System.Drawing.Common 8.0.8

## 项目结构

```
AiHUD/
├── Models/              # 数据模型
│   ├── AppData.cs
│   ├── AppItem.cs
│   ├── Category.cs
│   └── IconExtractor.cs
├── ViewModels/          # 视图模型
│   └── MainViewModel.cs
├── Views/               # 视图
│   ├── MainWindow.xaml
│   ├── AddCategoryDialog.xaml
│   └── EditAppDialog.xaml
├── Services/            # 服务层
│   ├── DataService.cs
│   └── AppLauncherService.cs
├── Converters/          # 值转换器
│   └── Converters.cs
├── App.xaml            # 应用配置
└── AiHUD.csproj        # 项目文件
```

## 快速开始

### 编译运行

```bash
cd AiHUD
dotnet restore
dotnet build
dotnet run
```

### 使用说明

1. **添加分类**: 点击左侧"添加分类"按钮，选择颜色和图标
2. **添加应用**: 选择分类后，点击"添加应用"按钮选择 .exe 文件
3. **启动应用**: 双击应用卡片即可启动程序
4. **编辑应用**: 右键点击应用卡片，选择"编辑"修改信息
5. **切换主题**: 点击左下角"切换主题"按钮

## 数据存储

应用数据存储在:
```
%LOCALAPPDATA%\AiHUD\data.json
```

## 界面预览

应用程序采用 Material Design 3 设计风格，提供现代化的用户体验：
- 左侧导航栏显示分类列表
- 右侧主区域以卡片形式展示应用
- 支持深色/浅色主题切换
