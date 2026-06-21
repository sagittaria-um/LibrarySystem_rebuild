# 运行说明

## 1. 环境要求

- Web 版：Windows、Linux、macOS 均可运行。
- 桌面版：仅支持 Windows 10/11，因为桌面壳使用 WinForms + WebView2。
- 数据库：SQL Server 2022 Docker，需 Docker Desktop 或 Docker Engine。
- 后端：.NET SDK 8。
- 前端：Node.js 20 或更高。
- 桌面运行时：WebView2 Runtime。

推荐演示环境是 Windows，可同时运行 Web 版、桌面版和安装包。Linux/macOS 适合运行 Web 版。

## 2. 启动数据库

在项目根目录执行：

```powershell
cd deploy
docker compose up -d
```

SQL Server 对外端口为 `14333`，默认连接串：

```text
Server=localhost,14333;Database=LibrarySystemDb;User Id=sa;Password=Library@2026;TrustServerCertificate=True;Encrypt=True
```

初始化脚本位于：

- `database/01_schema.sql`
- `database/02_seed.sql`

## 3. 构建前端

```powershell
cd ../client
npm install
npm run build
```

构建产物会输出到 `server/wwwroot`，由后端托管。

## 4. 启动后端 Web 系统

```powershell
cd ../server
dotnet run
```

浏览器访问：

```text
http://localhost:5297
```

Linux/macOS 命令相同，只需使用 `/` 作为路径分隔符。

## 5. 启动桌面端

在项目根目录执行：

```powershell
dotnet run --project desktop\LibrarySystem.Desktop.csproj
```

桌面端会自动启动本地后端服务，并用 WebView2 打开系统。

## 6. 构建发布目录

在项目根目录执行：

```powershell
.\deploy\build-release.ps1
```

发布结果位于 `deploy/out`。

若本机安装了 Inno Setup，可使用 `deploy/LibrarySystem.iss` 生成 Windows 安装包。

## 7. 默认账号

- 管理员：`admin / admin123`
- 读者：`2024001 / reader123`
- 读者：`2024002 / reader123`

读者登录后可进入“图书管理”，在可借图书行点击“借书”完成自助借阅。

## 8. 常见问题

- 登录失败：确认 SQL Server 容器已启动并完成初始化。
- 后端连接失败：确认 Docker 中 `library_sqlserver` 正在运行，且端口 `14333` 未被占用。
- 桌面端提示启动超时：先运行 `docker compose up -d`，再重新打开桌面端。
- 修改数据库脚本后想重建库：删除 Docker 卷 `librarysystem_library_sql_data` 后重新执行 `docker compose up -d`。
