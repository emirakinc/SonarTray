<div align="center">

<img src="docs/icon.png" width="84" alt="">

# SonarTray

**SteelSeries Sonar mikserini bildirim alanından yönet.**<br>
GG penceresini açmadan ses, mute, cihaz seçimi ve global kısayollar.

[![sürüm](https://img.shields.io/github/v/release/emirakinc/SonarTray?label=s%C3%BCr%C3%BCm&sort=semver&color=2ec4b6)](https://github.com/emirakinc/SonarTray/releases/latest)
[![ci](https://github.com/emirakinc/SonarTray/actions/workflows/ci.yml/badge.svg)](https://github.com/emirakinc/SonarTray/actions/workflows/ci.yml)
[![indirme](https://img.shields.io/github/downloads/emirakinc/SonarTray/total?label=indirme&color=2ec4b6)](https://github.com/emirakinc/SonarTray/releases)
[![lisans](https://img.shields.io/github/license/emirakinc/SonarTray?label=lisans&color=2ec4b6)](LICENSE)

<br>

<img src="docs/panel.png" width="540" alt="SonarTray mikser paneli: Ana Ses, Oyun, Sohbet, Medya ve Mikrofon kanalları">

</div>

<br>

## Ne yapar

- **Sağ tık** → kompakt mikser: Master / Game / Chat / Media / Mic için ses, mute ve çıkış/giriş cihazı seçimi
- **Sol tık** → SteelSeries GG penceresini öne getirir, kapalıysa başlatır
- **Global kısayollar** → oyundan çıkmadan mikrofonu sustur, ana sesi değiştir, paneli aç
- **Windows ile başlat** tiki (HKCU Run kaydı) ve panelden **Çıkış**
- GG kapanıp açıldığında portlar değişse bile **kendiliğinden yeniden bağlanır**; Stream modunda uyarı gösterir
- Tepsi ikonu ses seviyesine göre çizilir ve ekran ölçeği değişince yeniden üretilir

## Ekran görüntüleri

<table>
<tr>
<td width="50%" align="center">
  <img src="docs/hotkeys.png" width="100%" alt="Kısayol ayarları: her kısayola tıklayıp yeni kombinasyon atanabiliyor">
  <br><sub><b>Kısayollar</b> — panelden yeniden atanır; kombinasyonu başka uygulama kapmışsa satır işaretlenir</sub>
</td>
<td width="50%" align="center">
  <br>
  <img src="docs/osd.png" width="290" alt="Ekran üstü gösterge: Ana Ses %100">
  <br><br><sub><b>Ekran üstü gösterge</b> — kısayola bastığında kısa süre görünür</sub>
</td>
</tr>
</table>

## İndir

Son sürüm: **[Releases](https://github.com/emirakinc/SonarTray/releases/latest)**

| Paket | Kime |
|---|---|
| `SonarTray-vX.Y.Z-win-x64.zip` | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0/runtime) kurulu olanlar (~130 KB) |
| `SonarTray-vX.Y.Z-win-x64-self-contained.zip` | Runtime kurmak istemeyenler; tek başına çalışır (~58 MB) |

Zip'i aç, `SonarTray.exe`'yi `%LOCALAPPDATA%\Programs\SonarTray\` altına kopyala ve çalıştır. Kurulum
sihirbazı yok; kaldırmak için exe'yi ve `%LOCALAPPDATA%\SonarTray\` klasörünü silmek yeterli.

Paket imzalı olmadığı için SmartScreen "bilinmeyen yayıncı" diyebilir: **Daha fazla bilgi → Yine de
çalıştır**. İstersen indirdiğin zip'i release'teki `SHA256SUMS.txt` ile karşılaştır:

```powershell
Get-FileHash .\SonarTray-v0.1.0-win-x64.zip -Algorithm SHA256
```

## Kullanım

Windows 11 yeni tray ikonlarını varsayılan olarak `^` (gizli simgeler) menüsüne koyar; ikonu görev
çubuğuna sürükleyerek kalıcı olarak görünür yapabilirsin.

Varsayılan kısayollar — hepsi panelden değiştirilebilir:

| Kısayol | Ne yapar |
|---|---|
| `Ctrl+Shift+F8` | Paneli aç / kapat |
| `Ctrl+Shift+F9` | Mikrofonu sustur / aç |
| `Ctrl+Shift+F10` | Ana sesi sustur / aç |
| `Ctrl+Shift+F11` | Ana sesi azalt (%5) |
| `Ctrl+Shift+F12` | Ana sesi artır (%5) |

Ayarlar ve log: `%LOCALAPPDATA%\SonarTray\`

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

Sürüm numarası etiketten gelir; `release` iş akışı iki paketi de derleyip GitHub Release'i kendisi
oluşturur, sağlama toplamlarını ekler.

```bash
# 1) CHANGELOG.md'de "Yayımlanmadı" başlığını yeni sürüme çevir, csproj'daki <Version>'ı güncelle, commit'le
# 2) etiketi at
git tag -a v0.1.0 -m "SonarTray v0.1.0"
git push origin v0.1.0
```

Ön sürüm için `v0.2.0-beta.1` gibi bir etiket yeterli; iş akışı release'i otomatik olarak
*pre-release* işaretler. Etiket zaten varsa Actions → **release** → *Run workflow* ile elle de
tetikleyebilirsin.

## Nasıl çalışıyor

Sonar'ın yerel REST API'si resmi değildir. Keşif zinciri:

```
C:\ProgramData\SteelSeries\GG\coreProps.json
  └─ ggEncryptedAddress → https://127.0.0.1:{port}/subApps
       └─ subApps.sonar.metadata.webServerAddress → mikser uç noktaları
```

Tüm uç noktalar `Services/SonarClient.cs` içindedir; GG güncellemesiyle bir rota değişirse tek
dosyalık düzeltme.

| Klasör | İçerik |
|---|---|
| `Services/` | keşif, HTTP istemcisi, bağlantı durum makinesi, başlangıç kaydı, GG başlatıcı |
| `ViewModels/` | panel ve kanal satırı mantığı (debounce, senkron koruması) |
| `Views/` | popup penceresi, kanal satırı, kısayol ayarları, ekran üstü gösterge, konumlandırma |
| `Hotkeys/` | global kısayol kaydı ve atama |
| `Tray/` | NotifyIcon ve çalışma zamanında çizilen ikon |
| `Themes/Dark.xaml` | koyu tema ve kontrol stilleri |

## Lisans

[MIT](LICENSE) — SteelSeries ile bir ilgisi yoktur, resmi bir ürün değildir.
