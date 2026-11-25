using System.Collections.Generic;
using System.Web.Mvc;
using Wavelength.Web.Infrastructure;

namespace Wavelength.Web.Controllers
{
    // TEMPORARY: team manual-test checklist. Delete this file and Views/Qa/ before submitting the project.

    public class QaItem
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public string Link { get; set; }
        public string Account { get; set; }
    }

    public class QaSection
    {
        public string Title { get; set; }
        public string Note { get; set; }
        public List<QaItem> Items { get; set; }
    }

    /// <summary>Manual test checklist for the team (/qa). Signed-in users only, not linked from the site.</summary>
    [Authorize]
    public class QaController : BaseController
    {
        private static QaItem I(string id, string text, string link = null, string account = null)
        {
            return new QaItem { Id = id, Text = text, Link = link, Account = account };
        }

        [HttpGet, Route("qa")]
        public ActionResult Index()
        {
            var sections = new List<QaSection>
            {
                new QaSection { Title = "1. Kurulum", Note = "Bir kişi, temiz bir klonla.", Items = new List<QaItem>
                {
                    I("setup-f5", "Repo'yu klonla, Wavelength.sln'i aç, F5: paketler iner, site http://localhost:5281 adresinde açılır."),
                    I("setup-db", "İlk açılış birkaç saniye sürer, ana sayfa demo veriyle gelir.", "/"),
                    I("setup-tables", "Server Explorer: App_Data\\Wavelength.mdf içinde 19 tablo var."),
                    I("setup-offline", "İnternet kapalıyken de fontlar (Anton başlıklar) ve stiller yükleniyor.", "/"),
                }},
                new QaSection { Title = "2. Görünüm", Items = new List<QaItem>
                {
                    I("look-home", "Ana sayfa eski landing page ile aynı görünüyor.", "/"),
                    I("look-chars", "Özel karakterler her yerde doğru (♪ ✓ → · İ ğ ş ı), hiçbir yerde \"â™ª\" gibi bozukluk yok.", "/users/ibrahim"),
                    I("look-mobile", "Telefon genişliği (DevTools, 390 px): yana taşma yok, mobil menü açılıp kapanıyor.", "/explore"),
                    I("look-menu", "Sağ üstteki kullanıcı menüsü açılıyor; giriş/çıkışta yeşil bildirim çıkıp kayboluyor.", "/", "listener@"),
                }},
                new QaSection { Title = "3. Player", Items = new List<QaItem>
                {
                    I("play-start", "Bir parçada ▶: alttaki player açılıyor, ses geliyor, süre ilerliyor.", "/charts"),
                    I("play-controls", "İleri/geri, seek çubuğu ve ses ayarı çalışıyor; parça bitince sıradakine geçiyor.", "/charts"),
                    I("play-persist", "Başka sayfaya geçince player aynı parçada kalıyor (kendiliğinden çalmazsa ▶ yeterli).", "/explore"),
                    I("play-count", "~30 sn dinleyince dinlenme sayısı artıyor ve History'de görünüyor.", "/history", "listener@"),
                    I("play-like", "♥ anında renk değiştiriyor, parça Library'de görünüyor.", "/library", "listener@"),
                }},
                new QaSection { Title = "4. Form validasyonu", Items = new List<QaItem>
                {
                    I("form-register", "Register'da boş/yanlış alanlar: hatalar sayfa yenilenmeden kırmızı çıkıyor.", "/register"),
                    I("form-artist", "Register'da \"Artist\" seçince Stage name alanı açılıyor.", "/register"),
                    I("form-contact", "Contact'ta sayaç ve konu çipleri çalışıyor; mesaj admin Inbox'a düşüyor.", "/contact"),
                }},
                new QaSection { Title = "5. AJAX arama", Items = new List<QaItem>
                {
                    I("search-type", "Yazarken sonuçlar sayfa yenilenmeden geliyor.", "/search"),
                    I("search-filters", "Tür / genre / sıralama çipleri sonuçları güncelliyor.", "/search?q=a"),
                    I("search-url", "Adres çubuğundaki URL güncelleniyor, geri tuşu çalışıyor.", "/search"),
                }},
                new QaSection { Title = "6. Sanatçı stüdyosu", Items = new List<QaItem>
                {
                    I("studio-upload", "Gerçek bir MP3 yükle: süre otomatik okunuyor, kapak önizlemesi çıkıyor, en fazla 3 genre.", "/studio/upload", "kora@"),
                    I("studio-approve", "Submit for review → mod hesabıyla Review'da dinle ve onayla: parça herkese açılıyor, sanatçıya bildirim gidiyor.", "/admin/review", "mod@"),
                    I("studio-reject", "Sebep yazarak reddet → sanatçı sebebi görüyor, düzenleyip tekrar gönderiyor.", "/studio/tracks", "kora@"),
                    I("studio-albums", "Albüm oluştur, parça ekle; profil banner'ı yükle.", "/studio/albums", "kora@"),
                }},
                new QaSection { Title = "7. Kütüphane", Items = new List<QaItem>
                {
                    I("lib-playlist", "Playlist editörü: ara → ekle → sırala (↑ ↓) → kaydet; sıra korunuyor.", "/playlists/new", "listener@"),
                    I("lib-images", "Yüklenen avatar ve playlist kapağı görünüyor.", "/settings", "listener@"),
                    I("lib-notes", "Bildirime tıklayınca doğru sayfa açılıyor ve okundu oluyor.", "/notifications", "listener@"),
                }},
                new QaSection { Title = "8. Moderasyon ve admin", Items = new List<QaItem>
                {
                    I("admin-report", "Spam yorum raporunu resolve et: yorum parça sayfasından kayboluyor.", "/admin/reports", "mod@"),
                    I("admin-ban", "Bir kullanıcıyı banla: açık oturumu bir sonraki tıklamada düşüyor.", "/admin/users", "admin@"),
                    I("admin-charts", "Dashboard grafikleri dolu; audit log filtreleri çalışıyor.", "/admin", "admin@"),
                }},
                new QaSection { Title = "9. Erişim ve hata sayfaları", Items = new List<QaItem>
                {
                    I("err-login", "Giriş yapmadan /library → login sayfasına yönlendiriyor.", "/library"),
                    I("err-403", "Dinleyiciyle /admin → stilli 403 sayfası.", "/admin", "listener@"),
                    I("err-404", "Olmayan bir adres → stilli 404 sayfası.", "/this-page-does-not-exist"),
                    I("err-reset", "Forgot password: sayfada çıkan linkle şifre değişiyor.", "/forgot-password"),
                }},
                new QaSection { Title = "10. Sunum provası", Items = new List<QaItem>
                {
                    I("demo-flow", "Demo akışını bir kez baştan sona dene: kayıt → yükleme → onay → dinleme → playlist."),
                    I("demo-brief", "Herkes brifteki 10 maddenin nerede olduğunu gösterebiliyor (README tablosu)."),
                }},
            };
            ViewBag.Title = "Ekip test listesi";
            ViewBag.Robots = "noindex";
            return View(sections);
        }
    }
}
