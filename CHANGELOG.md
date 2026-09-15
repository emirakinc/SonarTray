# Değişiklikler

Biçim [Keep a Changelog](https://keepachangelog.com/tr/1.1.0/), sürümleme
[Semantic Versioning](https://semver.org/lang/tr/).

## [Yayımlanmadı]

## [0.2.0] — 2026-09-15

### Eklendi

- **İngilizce arayüz.** Varsayılan olarak sistem dili izlenir; ayarlardan Türkçe ya da İngilizce
  sabitlenebilir. İki dil de tek bir tablodan üretilir (`tools/Generate-Strings.py`)
- **Ayarlar sayfası** — dil, kısayol ses adımı, ekran üstü gösterge (açık/kapalı ve süre), tepsi
  fare tekerleği, Aux kanalı görünürlüğü, güncelleme kontrolü, log klasörünü açma
- **Tepsi ikonu üzerinde fare tekerleği** ile ana ses kontrolü
- **Oyun / Sohbet / Medya için kısayollar** (artır, azalt, sustur) — varsayılan olarak atanmamış
- **Ses profilleri** — Sonar'ın EQ ve efekt ön ayarları kanal satırından seçilebiliyor
- **Mikser ön ayarları** — tüm mikser durumu (ses, mute, yönlendirme) adıyla kaydedilip geri
  yüklenebiliyor
- **Classic ⇄ Stream mod değiştirme**
- **Güncelleme kontrolü** — günde bir kez GitHub Releases'e bakar, ön sürümleri stabil kullanıcıya
  önermez, atlanabilir ve tamamen kapatılabilir
- Aux kanalı artık ayarlardan görünür yapılabiliyor
- `--probe`: Sonar API haritasını canlı kurulumdan yeniden üretir (salt okunur)
- [docs/sonar-api.md](docs/sonar-api.md): doğrulanmış uç nokta haritası ve neyin bulunmadığı
- winget ve Scoop paket manifestleri ([packaging/](packaging/))
- Test paketi (265 test) ve CI'da format denetimi + test adımı
- `tools/Capture-Screenshots.ps1`: README ekran görüntülerini iki dilde yeniden üretir
  (`--show-settings`, `--show-hotkeys`, `--show-osd` geliştirme bayraklarıyla)

### Değişti

- Uygulama ikinci kez çalıştırıldığında sessizce çıkmak yerine çalışan örneğin panelini açıyor
- Panel üç sayfalı: Mikser / Ayarlar / Kısayollar. Esc her seferinde bir adım geri gider
- Log dosyası tek `.old` yerine üç kuşak saklıyor
- Ekran görüntüleri yeni arayüzle güncellendi; İngilizce README artık kendi görüntülerini kullanıyor

### Düzeltildi

- `Ctrl+Shift` gibi yalnızca değiştirici tuşlardan oluşan bir kombinasyon global kısayol olarak
  kaydedilebiliyordu. `KeyGestureConverter` bunu `Key=LeftShift + Modifiers=Control` olarak
  ayrıştırıyor, `RegisterHotKey` de kabul ediyordu — sonuç, sistemdeki tüm `Ctrl+Shift+X`
  kısayollarının yutulmasıydı
- Ayar ve kısayol nesneleri artık yüklendikleri dosyaya geri yazıyor; parametresiz `Save()` sabit
  kullanıcı yoluna yazdığı için geçici bir nesne gerçek yapılandırmayı ezebiliyordu
- `MixerViewModel` içindeki `CancellationTokenSource`'lar iptal ediliyor ama serbest bırakılmıyordu
- Kaydırma çubukları varsayılan açık renkli WPF stiliyle çiziliyordu; koyu temaya uyduruldu

## [0.1.0] — 2026-09-13

İlk sürüm.

### Eklendi

- Bildirim alanı ikonundan SteelSeries Sonar mikseri: Master / Game / Chat / Media / Mic için
  ses, mute ve çıkış/giriş cihazı seçimi
- Windows 11 görünümlü flyout panel ve ses seviyesine göre çizilen tepsi ikonu
- Global kısayol tuşları, ekran üstü onay göstergesi ve panelden yeniden atama
- **Windows ile başlat** seçeneği (HKCU Run kaydı)
- Sol tık ile SteelSeries GG penceresini öne getirme (kapalıysa başlatma)
- GG kapanıp açıldığında portlar değişse bile otomatik yeniden bağlanma; Stream modunda uyarı

### Düzeltildi

- Ekran ölçeği değiştiğinde tepsi ikonu boyutunun yeniden hesaplanması
- Başlık çubuğundaki ikon butonlarında tofu kutusu olarak çizilen ipuçları
- Ses kaydırıcısının her yerinden basıp sürükleyebilme

[Yayımlanmadı]: https://github.com/emirakinc/SonarTray/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/emirakinc/SonarTray/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/emirakinc/SonarTray/releases/tag/v0.1.0
