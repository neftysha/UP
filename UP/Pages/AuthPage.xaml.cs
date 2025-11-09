using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

// GDI+ для рендера
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace UP.Pages
{
    public partial class AuthPage : Page
    {
        private Users currentUser;

        // шаг 5: счётчик неверных попыток
        private int failedAttempts = 0;

        // локально храним ТЕКУЩИЙ код, показанный на картинке
        private string _captchaCode = string.Empty;

        public AuthPage()
        {
            InitializeComponent();

            // изначально скрываем капчу
            SetCaptchaVisible(false);

            // запретим Copy/Cut/Paste в поле ввода капчи (как в методичке)
            CommandManager.AddPreviewExecutedHandler(TextBoxCaptcha, TextBoxCaptcha_PreviewExecuted);
        }

        // ==== хэш пароля (SHA1 в верхнем регистре, как у вас) ====
        public static string GetHash(string password)
        {
            using (var hash = SHA1.Create())
                return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(password))
                                      .Select(x => x.ToString("X2")));
        }

        // ==== Войти ====
        private void ButtonAuth_Click(object sender, RoutedEventArgs e)
        {
            // 1) Если капча показана — проверяем сначала её
            if (CaptchaImage.Visibility == Visibility.Visible)
            {
                var input = TextBoxCaptcha.Text;
                if (string.IsNullOrWhiteSpace(input))
                {
                    MessageBox.Show("Введите код с картинки.", "Проверка капчи",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (!CaptchaService.EqualsNormalized(input, _captchaCode))
                {
                    MessageBox.Show("Неверно введён код с картинки.", "Проверка капчи",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    RefreshCaptcha(); // новый код и картинка
                    return;
                }
                // капча верна — продолжаем
            }

            // 2) проверка логина/пароля
            var login = TextBoxLogin.Text.Trim();
            var password = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Введите логин и пароль.", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string hashed = GetHash(password);

            using (var db = new Kamenetskiy_paymentEntities())
            {
                var user = db.Users.AsNoTracking()
                    .FirstOrDefault(u => u.Login == login && u.Password == hashed);

                if (user == null)
                {
                    failedAttempts++;

                    if (failedAttempts >= 3 && CaptchaImage.Visibility != Visibility.Visible)
                    {
                        MessageBox.Show("Слишком много неверных попыток. Введите капчу.", "Внимание");
                        ShowCaptcha();
                        return;
                    }

                    MessageBox.Show("Неверный логин или пароль.", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // успех
                failedAttempts = 0;
                SetCaptchaVisible(false);
                TextBoxCaptcha.Clear();

                currentUser = user;
                MessageBox.Show($"Добро пожаловать, {user.FIO}!", "Успех");
                NavigateByRole(user.Role);
            }
        }

        private void ButtonReg_Click(object sender, RoutedEventArgs e) =>
            NavigationService?.Navigate(new RegPage());

        private void ButtonChangePassword_Click(object sender, RoutedEventArgs e) =>
            NavigationService?.Navigate(new ChangePassPage());

        private void ButtonRefreshCaptcha_Click(object sender, RoutedEventArgs e) =>
            RefreshCaptcha();

        private void TextBoxLogin_TextChanged(object sender, TextChangedEventArgs e) { }

        // блокируем копипасту в поле капчи
        private void TextBoxCaptcha_PreviewExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Command == ApplicationCommands.Copy ||
                e.Command == ApplicationCommands.Cut ||
                e.Command == ApplicationCommands.Paste)
            {
                e.Handled = true;
            }
        }

        // ==== Капча: показать/спрятать/обновить ====
        private void ShowCaptcha()
        {
            SetCaptchaVisible(true);
            RefreshCaptcha();
        }

        private void SetCaptchaVisible(bool visible)
        {
            var v = visible ? Visibility.Visible : Visibility.Hidden;
            CaptchaImage.Visibility = v;
            TextBoxCaptcha.Visibility = v;
            ButtonRefreshCaptcha.Visibility = v;

            if (!visible)
                TextBoxCaptcha.Clear();
        }

        private void RefreshCaptcha()
        {
            _captchaCode = CaptchaService.MakeCode(5); // генерим код
            CaptchaImage.Source = CaptchaService.Render(_captchaCode, 220, 65); // рисуем именно его
            TextBoxCaptcha.Clear();
        }

        // ==== навигация по ролям ====
        private void NavigateByRole(string role)
        {
            switch (role)
            {
                case "User":
                    NavigationService?.Navigate(new UserPage(currentUser));
                    break;
                case "Admin":
                    NavigationService?.Navigate(new AdminPage());
                    break;
                default:
                    MessageBox.Show("Неизвестная роль пользователя", "Ошибка");
                    break;
            }
        }
    }

    /// <summary>
    /// Stateless-сервис капчи: генерация кода, рендер изображения,
    /// нормализация и сравнение с учётом русской раскладки. НИКАКОГО глобального состояния.
    /// </summary>
    internal static class CaptchaService
    {
        private static readonly Random _rnd = new Random();

        // Сгенерировать код
        public static string MakeCode(int len)
        {
            const string alphabet =
                "ABCDEFGHJKLMNPQRSTUVWXYZ" +   // без I/O
                "abcdefghijkmnpqrstuvwxyz" +   // без l/o
                "23456789";
            var buf = new char[len];
            for (int i = 0; i < len; i++)
                buf[i] = alphabet[_rnd.Next(alphabet.Length)];
            return new string(buf);
        }

        // Отрисовать картинку для конкретного кода
        public static BitmapImage Render(string code, int width, int height)
        {
            using (var bmp = Draw(code, width, height))
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                ms.Position = 0;

                var img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = ms;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                return img;
            }
        }

        // Сравнить с нормализацией (регистронезависимо)
        public static bool EqualsNormalized(string a, string b) =>
            Normalize(a).Equals(Normalize(b), StringComparison.OrdinalIgnoreCase);

        // Нормализация: трим + перевод похожих кириллических букв в латиницу
        public static string Normalize(string s)
        {
            if (s == null) return string.Empty;
            s = s.Trim();

            var map = new System.Collections.Generic.Dictionary<char, char>
            {
                ['а'] = 'a',
                ['А'] = 'A',
                ['е'] = 'e',
                ['Е'] = 'E',
                ['о'] = 'o',
                ['О'] = 'O',
                ['р'] = 'p',
                ['Р'] = 'P',
                ['с'] = 'c',
                ['С'] = 'C',
                ['х'] = 'x',
                ['Х'] = 'X',
                ['у'] = 'u',
                ['У'] = 'U', // ВАЖНО: к U/u, а не к Y/y
                ['к'] = 'k',
                ['К'] = 'K',
                ['м'] = 'm',
                ['М'] = 'M',
                ['т'] = 't',
                ['Т'] = 'T',
                ['н'] = 'h',
                ['Н'] = 'H',
                ['в'] = 'b',
                ['В'] = 'B',
                ['ї'] = 'i',
                ['Ї'] = 'I',
                ['і'] = 'i',
                ['І'] = 'I'
            };

            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                sb.Append(map.TryGetValue(ch, out var rep) ? rep : ch);

            return sb.ToString();
        }

        // Внутренний рендер
        private static Bitmap Draw(string text, int width, int height)
        {
            var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // фон
                using (var brush = new LinearGradientBrush(
                    new Rectangle(0, 0, width, height),
                    System.Drawing.Color.White,
                    System.Drawing.Color.FromArgb(240, 240, 240),
                    LinearGradientMode.Vertical))
                {
                    g.FillRectangle(brush, 0, 0, width, height);
                }

                // шум-линии
                for (int i = 0; i < 6; i++)
                {
                    using (var pen = new Pen(System.Drawing.Color.FromArgb(190, 190, 190), 1))
                    {
                        g.DrawLine(pen, _rnd.Next(width), _rnd.Next(height),
                                         _rnd.Next(width), _rnd.Next(height));
                    }
                }

                // текст
                using (var font = new System.Drawing.Font("Arial Black", 26, System.Drawing.FontStyle.Bold))
                using (var brush = new SolidBrush(System.Drawing.Color.FromArgb(
                    _rnd.Next(20, 80), _rnd.Next(20, 80), _rnd.Next(20, 80))))
                {
                    var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(text, font, brush, new RectangleF(0, 0, width, height), fmt);
                }

                // точки-шум
                for (int i = 0; i < 40; i++)
                {
                    bmp.SetPixel(_rnd.Next(width), _rnd.Next(height),
                        System.Drawing.Color.FromArgb(
                            _rnd.Next(150, 220),
                            _rnd.Next(150, 220),
                            _rnd.Next(150, 220)));
                }
            }
            return bmp;
        }
    }
}
