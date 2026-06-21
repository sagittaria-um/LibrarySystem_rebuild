# 简易图书管理系统

数据库原理课程大作业。项目实现中山大学深圳校区简易图书管理系统，包含 React Web 管理端、ASP.NET Core API、SQL Server 数据库脚本、WebView2 桌面壳和课程文档。

## 技术栈

- 前端：React + Vite + TypeScript
- 后端：ASP.NET Core 8 Web API + Dapper
- 数据库：SQL Server 2022 Docker
- 桌面壳：WinForms + WebView2

## 支持平台

| 场景 | Windows | Linux | macOS |
| --- | --- | --- | --- |
| Web 系统 | 支持 | 支持 | 支持 |
| SQL Server Docker | 支持 | 支持 | 支持 |
| 后端 API | 支持 | 支持 | 支持 |
| 前端构建 | 支持 | 支持 | 支持 |
| WebView2 桌面版 | 支持 | 不支持 | 不支持 |
| Windows 安装包 | 支持 | 不支持 | 不支持 |

推荐演示环境是 Windows，因为它同时支持 Docker SQL Server、Web 系统、WebView2 桌面壳和安装包。Linux/macOS 可以运行 Web 版，但不能运行 WinForms WebView2 桌面版。

## 默认账号

- 管理员：`admin / admin123`
- 读者：`2024001 / reader123`

读者登录后可在“图书管理”中自助借书；管理员可维护图书、读者、账号和借阅记录。

## 快速启动

以下命令均从项目根目录执行。请先启动 Docker Desktop，或在 Linux/macOS 环境中确认 Docker Engine 正在运行。

```powershell
cd deploy
docker compose up -d

cd ..\client
npm install
npm run build

cd ..\server
dotnet run
```

浏览器访问：`http://localhost:5297`

Linux/macOS 使用 `/` 路径分隔符：

```bash
cd deploy
docker compose up -d

cd ../client
npm install
npm run build

cd ../server
dotnet run
```

## Windows 桌面版

```powershell
dotnet run --project desktop\LibrarySystem.Desktop.csproj
```

桌面端会启动本地后端服务，并通过 WebView2 打开系统。

## 发布目录

```powershell
.\deploy\build-release.ps1
```

发布文件会生成到 `deploy/out`。如需安装包，可使用 Inno Setup 打开 `deploy/LibrarySystem.iss` 进行打包。

详细说明见 `docs/runbook.md`。
