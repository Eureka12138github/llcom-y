# 13 · MSIX 打包与签名（LLCOM-Y）—— 暂缓

> 状态：**暂缓（不发布 MSIX）**
> 本项目不打算上架微软商店，日常发布只提供 **exe / zip**，因此**不需要**安装包与签名。
> 工程身份已修正（不再冒用原作者的商店身份），但**不启用签名、不产出 MSIX**。
> 将来若确实需要"安装版"，再按本文档执行即可；证书脚本已保留。
>
> 仅发布 exe 时不需要改动本目录任何文件。

---

## 1. 当前身份

`WapProj/Package.appxmanifest`：

| 字段 | 值 | 说明 |
|---|---|---|
| `Identity Name` | `Eureka12138github.LLCOM-Y` | 包标识；侧载可自定义；上架商店时由 Partner Center 分配 |
| `Identity Publisher` | `CN=Eureka12138github` | **必须与签名证书的 Subject 完全一致** |
| `PublisherDisplayName` | `Eureka12138github` | 仅显示用 |
| `Version` | `2.0.0.0` | 4 段，每段 ≤ 65535 |
| `Application Id` | `App` | 保持不变 |

> 原作者的 Store 身份（`800948F61A16.llcom` / `CN=33E7C5B3-...`）已移除；`Package.StoreAssociation.xml` 的残留引用也已删除。

---

## 2. 为什么必须"身份 + 证书"一起改

MSIX 安装时会校验：**签名证书的 Subject == manifest 的 Publisher**。
只改其中一边会报证书不匹配（常见 `0x80073CF0` / `0x800B0109`）。
同时 `make-cert.ps1` 里的 `-Subject` 与 manifest 的 `Publisher` 必须一致。

---

## 3. 自签名证书（仅在需要侧载时生成）

仓库里**不保存任何证书文件**；需要打包时运行脚本现场生成：

| 文件 | 是否提交 | 用途 |
|---|---|---|
| `LLCOM-Y.cer` | ❌ 不提交（脚本生成） | 公钥证书，给用户安装信任 |
| `LLCOM-Y.pfx` | ❌ 忽略（`.gitignore`） | 私钥，用于签名 |
| `LLCOM-Y.pfx.password.txt` | ❌ 忽略 | 脚本生成的随机密码 |

```powershell
powershell -ExecutionPolicy Bypass -File WapProj\make-cert.ps1
```

脚本会输出 **Subject / Thumbprint / 有效期 / 密码文件位置**（证书写入当前用户的「个人」存储）。
改了 `Publisher` 或证书过期时，重新跑一次即可。

---

## 4. 打包（侧载）

1. **安装 VS 组件**：Visual Studio Installer → 修改 → 单个组件 → 搜索「MSIX」或「Windows 应用打包项目」。
   （当前工程 `TargetPlatformVersion` 为 `10.0.22621.0`，对应本机已安装的 SDK；装了 26100 可改回。）
2. 打开 `WapProj/WapProj.wapproj` → 右键 → **发布 → 创建应用包**。
3. 选择证书：
   - 从「当前用户\个人」存储选 `CN=Eureka12138github`（指纹见脚本输出），或
   - 选择文件 `WapProj/LLCOM-Y.pfx`，密码在 `LLCOM-Y.pfx.password.txt`。
4. 得到 `.msixbundle`（x86 + x64）后，用户安装：
   - 双击 `LLCOM-Y.cer` → 安装证书 → **本地计算机** → 「受信任人」；
   - 再双击 `.msixbundle`。

> 未签名的 MSIX 不能侧载安装。工程里 `AppxPackageSigningEnabled=False` 是默认值，创建应用包向导会自动写入证书配置。

---

## 5. CI 打包（可选）

把 `LLCOM-Y.pfx` 以 Base64 存进仓库 Secret，构建时还原并传参：

```powershell
msbuild WapProj\WapProj.wapproj /p:Configuration=Release /p:Platform=x64 `
  /p:AppxBundle=Never /p:AppxPackageSigningEnabled=true `
  /p:PackageCertificateKeyFile=WapProj\LLCOM-Y.pfx `
  /p:PackageCertificatePassword=$env:CERT_PASSWORD
```

---

## 6. 上架微软商店（可选，需要开发者账号）

1. 在 [Partner Center](https://partner.microsoft.com/) 保留应用名称，获得商店分配的 `Identity Name` / `Publisher`；
2. VS 里 项目 → 发布 → **Associate App with the Store**，登录后自动改写 manifest 身份；
3. 商店包由微软签名，无需自签名证书；
4. 商店版与本项目的 exe 版可以并存（包身份不同）。

---

## 7. 注意事项

- **exe 文件名必须保持 `llcom.exe`**（`ProfileInitializer` 有硬性检查）；显示名才是 LLCOM-Y。
- 改 `Publisher` 后必须重新生成证书，否则安装报证书不匹配。
- 自签名证书只适合侧载；公网分发建议使用正规代码签名证书或走商店。
- 升级包时只改 `Version`（4 段递增），不要改 `Identity`。
