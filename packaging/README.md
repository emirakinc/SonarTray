# Paket manifestleri

Bu klasördeki dosyalar kaynak sürümdür. Kullanıcıların `winget install` / `scoop install`
diyebilmesi için ayrıca **dış depolara gönderilmeleri** gerekir — bu depoya konmaları tek başına
yeterli değildir.

Her sürümde: `version`, indirme adresi ve SHA256 güncellenmeli. Sağlama toplamları release'teki
`SHA256SUMS.txt` dosyasından gelir; elle hesaplamaya gerek yok.

## winget

`manifests/e/emirakinc/SonarTray/<sürüm>/` altında üç YAML dosyası. Göndermeden önce doğrula:

```bash
winget validate --manifest packaging/winget/manifests/e/emirakinc/SonarTray/0.1.0
```

Yerelde kurup deneme (imzasız manifest olduğu için `--ignore-local-archive-malware-scan` gerekir):

```bash
winget install --manifest packaging/winget/manifests/e/emirakinc/SonarTray/0.1.0
```

Gönderim: [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) deposuna PR.
Klasör yapısı burada olduğu gibi `manifests/e/emirakinc/SonarTray/<sürüm>/` olmalı. PR açıldıktan
sonra otomatik doğrulama çalışır (paketi bir sanal alanda gerçekten kurar); ortalama birkaç gün
içinde birleşir.

Depo çok büyük olduğu için klonlamadan, API üzerinden göndermek pratiktir:

```bash
V=0.1.0
BRANCH="emirakinc.SonarTray-$V"
SHA=$(gh api repos/microsoft/winget-pkgs/git/ref/heads/master --jq '.object.sha')

# fork (ilk seferde) ve dalı upstream'e eşitle
gh repo fork microsoft/winget-pkgs --clone=false
gh api -X PATCH repos/emirakinc/winget-pkgs/git/refs/heads/master -f sha="$SHA" -F force=true
gh api -X POST  repos/emirakinc/winget-pkgs/git/refs -f ref="refs/heads/$BRANCH" -f sha="$SHA"

# üç manifesti dala yaz
cd packaging/winget/manifests/e/emirakinc/SonarTray/$V
for f in *.yaml; do
  gh api -X PUT "repos/emirakinc/winget-pkgs/contents/manifests/e/emirakinc/SonarTray/$V/$f" \
    -f message="New package: emirakinc.SonarTray version $V" \
    -f content="$(base64 -w0 < "$f")" \
    -f branch="$BRANCH"
done

gh pr create --repo microsoft/winget-pkgs \
  --head "emirakinc:$BRANCH" --base master \
  --title "New package: emirakinc.SonarTray version $V" \
  --body "Tray application for the SteelSeries Sonar mixer."
```

Paket, .NET 8 Desktop Runtime'a bağımlı olarak işaretlenmiştir — winget onu kendisi kurar, bu
yüzden küçük olan framework-dependent zip kullanılıyor.

## Scoop

`sonartray.json` tek dosyadır. Bir bucket deposuna (`bucket/sonartray.json` olarak) konur.
İki seçenek var:

1. **[ScoopInstaller/Extras](https://github.com/ScoopInstaller/Extras) deposuna PR** — README'deki
   `scoop install sonartray` komutunun çalışması için gereken budur, çünkü `extras` çoğu kullanıcıda
   ekli gelir. Yeni ve az bilinen uygulamalar bazen geri çevrilebiliyor.

2. **Kendi bucket'ın** (`emirakinc/scoop-bucket`) — her zaman çalışır, ama kullanıcı önce
   `scoop bucket add emirakinc https://github.com/emirakinc/scoop-bucket` demek zorunda.
   Bu yola gidersen README'deki kurulum komutunu da güncelle.

Önce (1)'i denemek mantıklı; geri çevrilirse (2) her hâlükârda duruyor.

Yerelde deneme:

```bash
scoop install .\packaging\scoop\sonartray.json
```

`checkver` ve `autoupdate` alanları sayesinde bucket'taki otomasyon yeni sürümü GitHub
Releases'ten ve sağlama toplamını `SHA256SUMS.txt`'ten kendisi alır.

## İmzalama

Paket imzalı değil, bu yüzden SmartScreen "bilinmeyen yayıncı" uyarısı verebilir. Tek gerçek
çözüm bir kod imzalama sertifikasıdır (OV için yıllık birkaç yüz dolar). winget ve scoop üzerinden
kurulum bu uyarıyı büyük ölçüde atlatır, çünkü indirme doğrulanmış sağlama toplamıyla yapılır.
