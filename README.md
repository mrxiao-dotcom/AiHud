# AI大本营

一个使用 WPF 和 Neumorphism（软浮雕）风格开发的 AI 工具分类管理桌面应用程序。

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
│   ├── MainWindow.xaml      # 主窗口
│   ├── AddCategoryDialog.xaml  # 添加分类对话框
│   ├── EditCategoryDialog.xaml # 编辑分类对话框
│   ├── EditAppDialog.xaml      # 编辑应用对话框
│   ├── MoveAppDialog.xaml      # 移动应用对话框
│   ├── AppMarketWindow.xaml    # 应用市场窗口
│   └── UpdateDialog.xaml       # 更新对话框
├── Services/            # 服务层
│   ├── DataService.cs
│   ├── AppLauncherService.cs
│   └── LicenseCache.cs
├── Converters/          # 值转换器
│   └── Converters.cs
├── NeumorphismTheme.xaml # Neumorphism 风格资源字典
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
5. **移动应用**: 右键点击应用卡片，选择"移动到"转移分类
6. **切换主题**: 点击左下角"切换主题"按钮

## 数据存储

应用数据存储在:
```
%LOCALAPPDATA%\AiDaBenYing\data.json
```

## 界面预览

应用程序采用 **Neumorphism（软浮雕）设计风格**，以柔和的阴影和圆角营造出立体浮雕感：

- **Neumorphism 风格**：光源从左上角照射，所有元素通过双重阴影（左亮右暗）实现凸起/凹陷效果
- **深色主题**：背景色 #2A2A38，左上亮阴影 #3A3A4C，右下暗阴影 #1E1E28
- **紫罗兰强调色**：#8B5CF6 主色调 + #06B6D4 青色辅助
- 左侧导航栏显示分类列表
- 右侧主区域以 Neumorphic 凸起卡片形式展示应用
- 所有交互态通过阴影翻转实现真实触感反馈（按下 = 内陷）
