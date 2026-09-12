# SonarTray

[![sürüm](https://img.shields.io/github/v/release/emirakinc/SonarTray?label=s%C3%BCr%C3%BCm&sort=semver)](https://github.com/emirakinc/SonarTray/releases/latest)
[![ci](https://github.com/emirakinc/SonarTray/actions/workflows/ci.yml/badge.svg)](https://github.com/emirakinc/SonarTray/actions/workflows/ci.yml)
[![indirme](https://img.shields.io/github/downloads/emirakinc/SonarTray/total?label=indirme)](https://github.com/emirakinc/SonarTray/releases)
[![lisans](https://img.shields.io/github/license/emirakinc/SonarTray?label=lisans)](LICENSE)

Windows bildirim alanından SteelSeries Sonar (GG) mikserini yöneten küçük bir araç (C# / WPF / .NET 8).

- **Sağ tık** → kompakt mikser paneli: Master / Game / Chat / Media / Mic için ses, mute ve çıkış/giriş cihazı seçimi
- **Sol tık** → SteelSeries GG penceresini öne getirir (kapalıysa açar)
- **Global kısayollar** → panelden yeniden atanabilir, ekran üstü onay gösterir
- Panelde **Windows ile başlat** tiki (HKCU Run kaydı) ve **Çıkış** butonu
- GG kapanıp açıldığında (portlar değişse bile) otomatik yeniden bağlanır; Stream modunda uyarı gösterir

## İndir

Son sürüm: **[Releases](https://github.com/emirakinc/SonarTray/releases/latest)**

| Paket | Kime |
|---|---|
| `SonarTray-vX.Y.Z-win-x64.zip` | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0/runtime) kurulu olanlar (~130 KB) |
| `SonarTray-vX.Y.Z-win-x64-self-contained.zip` | Runtime kurmak istemeyenler; tek başına çalışır (~58 MB) |

Zip'i aç, `SonarTray.exe`'yi `%LOCALAPPDATA%\Programs\SonarTray\` altına kopyala ve çalıştır.
Kurulum sihirbazı yok; kaldırmak için exe'yi ve `%LOCALAPPDATA%\SonarTray\` klasörünü silmek yeterli.

Paket imzalı olmadığı için SmartScreen "bilinmeyen yayıncı" diyebilir: **Daha fazla bilgi → Yine de çalıştır**.
İstersen indirdiğin zip'in SHA-256'sını release'teki `SHA256SUMS.txt` ile karşılaştır:

```powershell
Get-FileHash .\SonarTray-v0.1.0-win-x64.zip -Algorithm SHA256
```

Windows 11 yeni tray ikonlarını varsayılan olarak `^` (gizli simgeler) menüsüne koyar; ikonu görev çubuğuna
sürükleyerek kalıcı olarak görünür yapabilirsin.

## Geliştirme

```bash
dotnet build
dotnet run
dotnet run -- --smoke      # API duman testi; sonuç %LOCALAPPDATA%\SonarTray\sonartray.log
dotnet run -- --show       # paneli açılışta göster (geliştirme kolaylığı)
dotnet run -- --verbose    # her yazma isteğini logla
dotnet publish -c Release  # bin\Release\net8.0-windows\win-x64\publish\SonarTray.exe
powershell -ExecutionPolicy Bypass -File tools\Make-Icon.ps1   # Assets\tray.ico'yu yeniden üret
```

Her push ve PR'da `ci` iş akışı Release derlemesi yapar (`-warnaserror`) ve `SonarTray.exe`'yi
çalıştırmanın **Artifacts** bölümüne koyar — etiket çıkarmadan denemek için.

### Sürüm çıkarma

Sürüm numarası etiketten gelir; `release` iş akışı iki paketi de derleyip GitHub Release'i kendisi oluşturur.

```bash
# 1) CHANGELOG.md'de "Yayımlanmadı" başlığını yeni sürüme çevir, csproj'daki <Version>'ı güncelle, commit'le
# 2) etiketi at
git tag -a v0.1.0 -m "SonarTray v0.1.0"
git push origin v0.1.0
```

Ön sürüm için `v0.2.0-beta.1` gibi bir etiket yeterli; iş akışı release'i otomatik olarak *pre-release*
işaretler. Etiket zaten varsa Actions → **release** → *Run workflow* ile elle de tetikleyebilirsin.

## Nasıl çalışıyor

Sonar'ın yerel REST API'si resmi değildir. Keşif: `C:\ProgramData\SteelSeries\GG\coreProps.json` →
`ggEncryptedAddress` → `https://127.0.0.1:{port}/subApps` → `subApps.sonar.metadata.webServerAddress`.
Tüm uç noktalar `Services/SonarClient.cs` içindedir; GG güncellemesiyle bir rota değişirse tek dosyalık düzeltme.

| Klasör | İçerik |
|---|---|
| `Services/` | keşif, HTTP istemcisi, bağlantı durum makinesi, başlangıç kaydı, GG başlatıcı |
| `ViewModels/` | panel ve kanal satırı mantığı (debounce, senkron koruması) |
| `Views/` | popup penceresi, kanal satırı, konumlandırma |
| `Themes/Dark.xaml` | koyu tema ve kontrol stilleri |
| `Tray/` | NotifyIcon ve çalışma zamanında çizilen ikon |
| `Hotkeys/` | global kısayol kaydı ve atama |

## Lisans

[MIT](LICENSE) — SteelSeries ile bir ilgisi yoktur, resmi bir ürün değildir.
