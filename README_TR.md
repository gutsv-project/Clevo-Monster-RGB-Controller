# GutsV Colour 🎨
> **Monster ve Clevo Laptoplar İçin Yeni Nesil RGB Kontrol Merkezi**

**GutsV Colour**, standart Control Center yazılımına kıyasla çok daha hafif, hızlı ve estetik bir alternatiftir. Monster Notebook ve Clevo altyapılı cihazlar için özel olarak geliştirilmiş bu yazılım, klavye ışıklandırmanızı özgürleştirir.

![GutsV Önizleme](assets/preview.png)

## ⚡ Neden GutsV?
Orijinal yazılımlar genellikle hantal ve yavaştır. GutsV ise performans odaklıdır:
*   **Sıfır Gecikme:** Özel Anti-Lag sürücüsü sayesinde ışık değişimleri anlıktır, arayüz asla donmaz.
*   **Ultra Hafif:** Arka planda çalışırken sistem kaynaklarını neredeyse hiç tüketmez.
*   **Akıllı Entegrasyon:** Mevcut sisteminizle uyumlu çalışır, ekstra sürücü kurmanıza gerek kalmaz.

## 🚀 Öne Çıkan Özellikler
*   **🎵 Müzik Duyarlı Mod (Audio Sync):** Klavyeniz çalan müziğin ritmine göre anlık ses frekanslarına uyumlu olarak dans etsin.
*   **🖥️ Ekran Ambiyansı (Ambient Lighting - Screen Sync):**
    *   **%100 Tam Ekran Senkronizasyonu:** High-DPI (PerMonitorV2) uyumlu, pencere kenarları, görev çubuğu ve köşeler dahil tüm ekranı uçtan uca tarar.
    *   **Kromatik Ağırlıklı Örnekleme (Chroma Weighting):** Beyaz arka planların, menülerin ve alt yazıların renkleri boğmasını engeller; ekrandaki canlı ve renkli alanlara karesel öncelik tanır.
    *   **HSV Doygunluk ve Canlılık Güçlendirici:** Klavye LED'lerinin soluk veya süt beyazı görünmesini önler; film ve oyunlarda canlı, doygun renkler yansıtır.
    *   **Akıllı Belge ve Karanlık Sahne Koruması:** Word, PDF veya Not Defteri gibi tek renkli ekranlarda gözü yormayan yumuşak nötr beyaz aydınlatmaya geçer; karanlık sinematik sahnelerde klavyeyi otomatik olarak karartır.
    *   **Sinematik EMA Yumuşatması:** 60 FPS hızında yumuşak renk geçişleri sağlayarak ani titremeleri ve göz yorgunluğunu ortadan kaldırır.
*   **🌡️ Sistem Monitörü ve Isı Haritası (Heatmap):**
    *   **Mikrosaniyelik CPU Yükü:** Düşük seviyeli doğrudan çekirdek API'si (`GetSystemTimes`) ile takılmasız ve kesin işlemci kullanımı ölçümü.
    *   **Gerçek Donanım Sıcaklığı:** WMI ACPI termal bölge sensörü üzerinden donanımın anlık sıcaklığını (°C) takip eder.
    *   **5 Kademeli Akıcı Isı Haritası:** Cyan -> Yeşil -> Sarı -> Turuncu -> Kırmızı renk geçişleriyle donanım durumunu dinamik ve pürüzsüz yansıtır.
*   **💾 Hafıza Modu:** Bilgisayarı kapattığınızda son renginizi ve animasyonunuzu hatırlar. Açılışta kaldığı yerden devam eder.
*   **🤖 Otomatik Başlatma:** Windows ile sessizce başlar, yönetici izni sormaz ve donanım hazır olana kadar akıllıca bekler.
*   **🌌 Animasyon Motoru:**
    *   **Nefes Alma (Breathe / Fresh Breathe):** Yumuşak ve organik geçiş efektleri.
    *   **Renk Dönüşümü (Color Shift / Transform):** Gökkuşağı ve kesintisiz renk akışı modları.
    *   **Uyarıcı Yanıp Sönme (Pulsating Blink):** Dinamik uyarı efektleri.
*   **✨ Cyberpunk Arayüz:** Oyuncular için tasarlanmış şık ve modern karanlık tema.

## 💻 Uyumluluk
Özellikle **Monster Notebook** ve **Clevo** kasalı cihazlar için tasarlanmıştır:
*   **Insyde DCHU Sürücüleri** (Modern modellerin çoğu)
*   **WMI Arayüzü** (Eski/Bazı özel modeller)
*   *Not: Tek Bölgeli ve Çok Bölgeli klavyeleri otomatik algılar.*

## 📦 Kurulum ve Kullanım
**GutsV taşınabilir (portable) bir uygulamadır.** Kurulum sihirbazı ile uğraşmazsınız.

1.  En güncel sürümü indirin.
2.  Klasörü kalıcı bir yere (Örn: `C:\GutsV`) çıkartın.
3.  **`GutsV Colour.exe`** dosyasını çalıştırın.
4.  **İlk Çalıştırma:** Size **"Enable Service Mode"** (Servis Modunu Aç) diye soracaktır. **EVET** deyin.
    *   *Bu, programın her açılışta otomatik ve sorunsuz çalışmasını sağlar.*

> **Önemli:** Orijinal Control Center (FnKey.exe) uygulamanızın arka planda açık olması önerilir. GutsV, donanımla iletişim kurmak için bu servislere ihtiyaç duyabilir.

## 🎮 Kullanım İpuçları
*   **Sistem Tepsisi (Tray):** Programı kapattığınızda (X), sağ alt köşeye (saat yanı) küçülür. Çift tıklayarak tekrar açabilirsiniz.
*   **Sıfırlama:** Programı kaldırmak isterseniz, üst menüdeki **Çöp Kutusu** ikonuna tıklayın. Bu işlem tüm ayarları ve otomatik başlatma görevini siler.
*   **Animasyonlar:** Listeden bir efekt seçin ve "START" butonuna basın.

## 🛠️ Sorun Giderme
*   **"Device Not Found" Hatası:** Laptopunuzun orijinal Control Center yazılımının çalıştığından emin olun. GutsV'yi yeniden başlatın.
*   **Otomatik Başlamıyor:** GutsV'yi açın, Çöp Kutusu ikonuna basarak temizleyin, sonra kapatıp tekrar yönetici olarak açın ve "Service Mode" sorusuna Evet deyin.

## ⚖️ Yasal Uyarı
Bu yazılım donanım sürücüleriyle iletişim kurar. Kapsamlı testlerden geçmiştir ancak kullanım sorumluluğu size aittir.

---
*Geliştirici: acerhizm*
