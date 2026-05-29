<p align="center">
  <img src="Properties/thumb.png" width="120" alt="JellyfinProxy" />
</p>

<h1 align="center">JellyfinProxy</h1>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&style=flat-square" />
  <img src="https://img.shields.io/badge/Jellyfin-10.11+-AA5CC3?logo=jellyfin&style=flat-square" />
  <img src="https://img.shields.io/badge/license-MIT-blue?style=flat-square" />
  <img src="https://img.shields.io/badge/dependencies-zero-success?style=flat-square" />
</p>

<p align="center"><b>Jellyfin 网络优化插件</b><br>HTTP 选择性代理，解决刮削元数据时 TMDB / TVDB / FanArt 的网络连通问题<br>支持 Jellyfin 10.11+</p>

---

## ✨ 功能

🌐 **选择性代理** — 仅对白名单域名走 HTTP 代理，其余流量直连，不影响内网速度。

默认代理域名：`api.themoviedb.org` / `image.tmdb.org` / `api.tvdb.com` / `fanart.tv` 等。

---

## ⚡ 安装

### 方法一：插件仓库安装（推荐）

1. Jellyfin 控制台 → 插件 → 仓库 → 添加
2. 填入仓库地址：
   ```
   https://raw.githubusercontent.com/tardlk/JellyfinProxy/master/manifest.json
   ```
3. 在插件目录中找到 **JellyfinProxy**，点击安装
4. 重启 Jellyfin

### 方法二：手动安装

1. 从 [Releases](../../releases) 下载 `JellyfinProxy.zip`
2. 解压到 Jellyfin 插件目录 `data/plugins/JellyfinProxy/`
3. 重启 Jellyfin

---

## 🔧 配置

安装后在 **控制台 → 插件 → JellyfinProxy** 进行配置：

| 配置项 | 说明 |
|--------|------|
| 启用代理服务器 | 开关 |
| 代理服务器地址 | `http://user:pass@host:port` |
| 代理域名列表 | 一行一个域名，默认已填常用元数据站点 |
| 调试模式 | 开启后输出详细请求日志 |

> ⚠️ 修改配置后需重启 Jellyfin 生效。

---

## 📦 本地编译

```bash
git clone https://github.com/tardlk/JellyfinProxy.git
cd JellyfinProxy
dotnet build -c Release
```

需要 .NET 9 SDK，产物：`bin/Release/net9.0/JellyfinProxy.dll`。

---

## 📄 License

MIT © [tardlk](https://github.com/tardlk)
