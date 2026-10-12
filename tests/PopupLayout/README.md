# Popup yerleşim testleri

Windows üzerinde .NET Framework 4.7.2 Developer Pack ve Visual Studio MSBuild ile çalıştırın:

```powershell
msbuild tests/PopupLayout/PopupLayoutTests.csproj /t:Rebuild /verbosity:minimal
& tests/PopupLayout/bin/PopupLayoutTests.exe
```

Testler gerçek WinForms kontrollerini kullanır; Outlook veya VSTO gerektirmez. Mail giriş ve onay pencerelerini varsayılan, dar ve geniş boyutlarda, tekrar daraltıldığında ve %150 ölçekle kontrol eder. Boş konu doğrulaması, arka plan parçalarının canlı önizlemeye aktarılması, son parçanın hazır durumunda görünmesi, kısmi yanıtın onaylanamaması, hazır durumuna geçiş, gerçek ayarlar penceresinin iki sekmesi, servis adları, servis değişiminde token/sertifika alanlarının devre dışı kalması ve token’ın korunması, Kaydet/Vazgeç düğmeleri, iki sütunlu yerleşim, uzun kesintisiz metin ve açıklamanın değişmesi de kapsanır.

macOS Mono'nun Carbon WinForms sürücüsü bu testleri çalıştırmak için uygun değildir. Derleme kontrolü görsel doğrulamanın yerine geçmez. Windows'ta ayrıca Outlook içinden üç popup'ı %100, %150 ve %200 ekran ölçeklerinde açın; pencereyi daraltıp genişletin, açıklamalar ile düğmelerin kırpılmadığını ve küçük yükseklikte dikey kaydırmayla erişilebilir olduğunu kontrol edin.

Ayarlar penceresi de bu projede gerçek üretim sınıfıyla derlenir. GUI açmadan
üç sertifika modunun veri seçimini, paket temizlemenin diğer modları
etkilemediğini ve gömülü ikonun pencere/şerit boyutlarında yüklenmesini
kontrol etmek için derlemeden sonra:

```sh
mono tests/PopupLayout/bin/PopupLayoutTests.exe --settings-only
```

Bu kontrol ayar dosyasına yazmaz. Windows üzerinde yeni paket modunu seçme,
zincir yükleme, Kaydet ve yeniden açma adımlarını ayrıca doğrulayın.

Markdown değişikliğinden sonra test projesi NuGet restore gerektirir:

```powershell
msbuild tests/PopupLayout/PopupLayoutTests.csproj /t:Restore,Rebuild /verbosity:minimal
```

Önizleme artık salt okunur RichTextBox kullanır. Testler canlı ve tamamlanmış
Markdown biçimlerini, satır sonlarını, düz metin hedef önizlemesini ve yeni
Mail biçimlendirme sekmesini de kapsar. `--settings-only` geçici, sentetik bir
ayar dosyasında Markdown seçiminin kaydedilip yeniden yüklenmesini doğrular;
kullanıcının ayar dosyasına yazmaz. Gerçek RichEdit görünümü ve Outlook RTF
aktarımının Windows üzerinde ayrıca kontrol edilmesi gerekir.

macOS üzerinde, Windows doğrulamasının yerine geçmeyen bağımsız bir RTF
okuyucu kontrolü de çalıştırılabilir. Fixture’lar yalnız sentetik içeriktir:

```sh
dotnet run --project tests/TransportTests.csproj -- --rtf-fixtures /tmp/askai-rtf-fixtures
python3 tests/verify_rtf_fixtures.py /tmp/askai-rtf-fixtures
```

Bu kontrol macOS `textutil` okuyucusundan gerçek liste, paragraf girintisi,
tablo hücresi, vurgu, Unicode, bağlantı ve kod çıktısını kontrol eder.
Windows’ta dar/geniş pencerelerde Markdown tablo önizlemesini ve akış
sırasında yukarı kaydırılan metnin konumunu ayrıca doğrulayın.

Hafıza testleri eski kayıtların boş hafızayla yüklenmesini, çok satırlı Unicode
metnin şifreli kaydetme/yükleme döngüsünü, kapatma/temizleme davranışını,
4.000 karakter sınırını ve bozuk kaydın güvenli hata mesajını kapsar.
`--settings-only` bu kontrolleri sentetik geçici dosyalarda çalıştırır.
Windows DPAPI davranışı ayrıca Windows üzerinde doğrulanmalıdır.
Arayüz kontrolleri Hafıza sekmesinin alanını, sayacını ve Temizle düğmesini
kapsar. Windows'ta Enter ile yeni satır, uzun metin yapıştırma, Kaydet/Vazgeç,
yeniden açma ve gerçek Outlook üretiminde kişisel üslup etkisini de doğrulayın.
