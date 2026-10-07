namespace Askai
{
    public static class AppConfig
    {
        // ── AI API Ayarları ──────────────────────────────────
        // Kendi API adresinizi buraya yazın
        // OpenAI uyumlu: https://api.openai.com/v1/chat/completions
        // Ollama lokal:  http://localhost:11434/api/chat
        // LM Studio:    http://localhost:1234/v1/chat/completions
        public static string ApiUrl = "http://localhost:5100/ask";

        // API anahtarı (gerekiyorsa)
        public static string ApiKey = "";

        // Model adı
        public static string Model = "TAB 3.2";

        // Sistem promptu - AI'ın nasıl yanıt vereceğini belirler
        public static string SystemPrompt =
            "Sen profesyonel bir e-posta asistanısın. " +
            "Sana gönderilen e-postaya uygun, profesyonel ve nazik bir yanıt yaz. " +
            "Sadece yanıt metnini yaz. Konu satırı veya meta bilgi ekleme. " +
            "Selamlama ve kapanış cümlesi ekle. " +
            "Placeholder koyma, ekleme.";

        // İstek zaman aşımı (dakika)
        public static int TimeoutMinutes = 5;
    }
}