# SonarTray

Windows bildirim alanından SteelSeries Sonar (GG) mikserini yöneten küçük bir araç (C# / WPF / .NET 8).

- **Sağ tık** → kompakt mikser paneli: Master / Game / Chat / Media / Mic için ses, mute ve çıkış/giriş cihazı seçimi
- **Sol tık** → SteelSeries GG penceresini öne getirir (kapalıysa açar)
- Panelde **Windows ile başlat** tiki (HKCU Run kaydı) ve **Çıkış** butonu
- GG kapanıp açıldığında (portlar değişse bile) otomatik yeniden bağlanır; Stream modunda uyarı gösterir

## Kurulum / kullanım

Yayınlanan tek dosya: `%LOCALAPPDATA%\Programs\SonarTray\SonarTray.exe` (.NET 8 Desktop Runtime gerektirir).

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
