# Sonar yerel API notları

SteelSeries Sonar'ın yerel REST API'si **resmi değildir** ve belgelenmemiştir. Buradaki her şey
çalışan bir kurulumda (GG 2026.x, Sonar classic modda) `OPTIONS` ve `GET` istekleriyle doğrulandı.
Yazma fiilleri `OPTIONS` yanıtındaki `Allow` başlığından okundu; hiçbiri kör denenmedi.

Bir GG güncellemesi bunları değiştirebilir. `SonarTray.exe --probe` bu tabloyu yeniden üretir.

## Keşif zinciri

```
C:\ProgramData\SteelSeries\GG\coreProps.json
  └─ ggEncryptedAddress            → https://127.0.0.1:{port}/subApps   (kendinden imzalı sertifika)
       └─ subApps.sonar.metadata.webServerAddress → http://127.0.0.1:{port}   (düz HTTP)
```

Her iki port da GG her yeniden başladığında değişir.

## Uç noktalar

### Mod

| Fiil | Yol | Not |
|---|---|---|
| `GET` | `/mode` | `"classic"` veya `"stream"` (tırnaklı JSON dizesi) |
| `PUT` | `/mode/{classic\|stream}` | Sonar'ı modlar arasında geçirir. Gerçekten çalıştırılarak doğrulandı: classic → stream → classic, ikisi de 200 |

### Ses — classic mod

| Fiil | Yol |
|---|---|
| `GET` | `/volumeSettings/classic` |
| `PUT` | `/volumeSettings/classic/{volumeId}/Volume/{0..1}` |
| `PUT` | `/volumeSettings/classic/{volumeId}/Mute/{true\|false}` |

`volumeId`: `master`, `game`, `chatRender`, `chatCapture`, `media`, `aux`

### Ses — stream mod

| Fiil | Yol |
|---|---|
| `GET` | `/volumeSettings/streamer` |
| `PUT` | `/volumeSettings/streamer/{volumeId}/{streaming\|monitoring}/Volume/{0..1}` |
| `PUT` | `/volumeSettings/streamer/{volumeId}/{streaming\|monitoring}/isMuted/{true\|false}` |

> **Dikkat:** classic `Mute` derken stream `isMuted` diyor. Simetrik değil; bu bir yazım hatası
> değil, API gerçekten böyle. `Mute`, `mute` ve `Muted` varyantlarının hepsi 404 döner.

### Cihazlar ve yönlendirme

| Fiil | Yol | Not |
|---|---|---|
| `GET` | `/audioDevices` | Gerçek + Sonar'ın sanal cihazları |
| `GET` | `/classicRedirections` | |
| `PUT` | `/classicRedirections/{redirectionId}/deviceId/{deviceId}` | Bazı sürümlerde `POST`; istemci ikisini de dener |
| `GET` | `/streamRedirections` | Üç kayıt: `streaming`, `monitoring` ve `mic` |
| `PUT` | `/streamRedirections/{streaming\|monitoring}/deviceId/{deviceId}` | |

Her `streamRedirections` kaydı bir de `status` dizisi taşır: hangi kanalların o alt mikse
katıldığını söyler (ör. `streaming` yalnızca `chatCapture`, `monitoring` ise
`chatRender,game,media,aux`).

`redirectionId`: `game`, `chat`, `media`, `aux`, `mic` — ses kimlikleriyle **aynı değil**
(`chat` ↔ `chatRender`, `mic` ↔ `chatCapture`).

Gerçek cihazları Sonar'ın sanal uç noktalarından ayırmak için: `role == "none"` ve `isVad == false`.

### Ses profilleri (EQ / efekt ön ayarları)

| Fiil | Yol | Not |
|---|---|---|
| `GET` | `/configs` | Her sanal cihaz için tüm profiller — **büyük** (test edilen makinede ~1,3 MB / 391 kayıt, 5 cihaz) |
| `GET` | `/configs/selected` | Sanal cihaz başına seçili profil; küçük ve ucuz |
| `PUT` | `/configs/{id}/select` | Profili kendi cihazına uygular |
| `DELETE` | `/configs/{id}` | Profili siler — SonarTray bunu kullanmaz |

Profil `data` alanı: `bassBoostState`, `trebleBoostState`, `voiceClarityState`, `smartVolume`,
`generalGain`, `parametricEQ`, `virtualSurroundState`, `virtualSurroundChannels`, `reverbGainDB`,
`formFactor`, `globalEnableState`.

`/configs` boyutu yüzünden tam liste yalnızca kullanıcı profil menüsünü açtığında çekilir;
durum takibi `/configs/selected` üzerinden yapılır.

### Uygulama başına yönlendirme

| Fiil | Yol |
|---|---|
| `GET` | `/audioDeviceRouting` |

Cihaz başına `audioSessions` dizisi döner: `processName`, `processId`, `displayName`,
`isSystemSound`, `state`, `routingErrorDetected`.

**Salt okunur.** `/audioDeviceRouting/{...}/{...}` yolları `Allow: GET` döner — bu API üzerinden
bir uygulamayı başka kanala taşımak mümkün değil, yalnızca nerede olduğu okunabilir.

## Bulunmayanlar

Aşağıdakiler arandı ve **yok** (hepsi 404):

- **ChatMix** — `/chatMix`, `/chatMixState`, `/gameChatBalance` ve benzerleri. ChatMix bir donanım
  özelliği (Arctis kadranı); Sonar'ın yazılım API'sinde karşılığı bulunmuyor.
- Olay akışı — `/subscribe`, `/events`, `/ws`, `/notifications`. Push bildirimi yok;
  yoklama (polling) tek seçenek.
- Keşif/dokümantasyon uçları — `/`, `/swagger`, `/api`, `/help`.
