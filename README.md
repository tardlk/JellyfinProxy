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

<p align="center"><b>Jellyfin 网络优化插件</b><br>解决刮削元数据时 TMDB / TVDB / FanArt 的网络连通问题<br>支持 Jellyfin 10.11+</p>

---

## ✨ 功能

| 功能 | 说明 |
|------|------|
| 🌐 选择性代理 | 仅对白名单域名走 HTTP 代理，其余直连 |
| 🔄 替代 TMDB | 将 TMDB API 和图片请求重定向到自定义地址 |
| 📡 强制 IPv4 | 对指定域名跳过 IPv6，避免超时 |

---

## ⚡ 安装

1. 从 Releases 下载 `JellyfinProxy.dll`
2. 复制到 Jellyfin `data/plugins/JellyfinProxy/`
3. 重启 Jellyfin
4. 在 **控制台 → 插件 → JellyfinProxy** 进行配置

> ⚠️ 修改配置后需重启 Jellyfin 生效。

---

## 🔧 配置说明

### 代理服务器
- **代理服务器地址**：`http://user:pass@host:port`
- **代理域名列表**：一行一个，默认 TMDB / TVDB / FanArt

### 替代 TMDB
- 将 `api.themoviedb.org` 和 `image.tmdb.org` 重定向到自建镜像站

### 强制 IPv4
- 对指定域名仅解析 IPv4，默认 `image.tmdb.org`

---

## 📦 本地编译

```bash
git clone https://github.com/tardlk/JellyfinProxy.git
cd JellyfinProxy
dotnet publish -c Release
```

需要 .NET 9 SDK。

---

## 📄 License

MIT
