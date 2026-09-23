using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using SeleniumExtras.WaitHelpers;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.Versioning;
using System.Security.Principal;
using WebMetroSR.Interfaces;

namespace WebMetroSR.Services
{
    public class MetroScopeService : IMetroScopeService
    {
        private readonly string _chromeDriverPath;
        private const int WAIT_TIMEOUT_SECONDS = 15; // увеличен таймаут

        public MetroScopeService()
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _chromeDriverPath = Path.Combine(baseDirectory, "Drivers", "chromedriver.exe");

            if (!File.Exists(_chromeDriverPath))
            {
                throw new FileNotFoundException($"chromedriver.exe не найден по пути: {_chromeDriverPath}. " +
                    "Скопируйте chromedriver.exe в папку Drivers вашего приложения.");
            }
        }

        private static ChromeOptions GetChromeOptions()
        {
            var options = new ChromeOptions();
            options.AddArgument("--headless=new");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--blink-settings=imagesEnabled=false");
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddExcludedArgument("enable-automation");
            options.AddAdditionalOption("useAutomationExtension", false);
            return options;
        }

        private ChromeDriver CreateChromeDriver()
        {
            var service = ChromeDriverService.CreateDefaultService(
                Path.GetDirectoryName(_chromeDriverPath),
                Path.GetFileName(_chromeDriverPath)
            );
            service.SuppressInitialDiagnosticInformation = true;
            service.HideCommandPromptWindow = true;

            var options = GetChromeOptions();
            return new ChromeDriver(service, options);
        }

        /// <summary>
        /// Устанавливает переменные curUser на странице MetroScope.
        /// </summary>
        [SupportedOSPlatform("windows")]
        private void SetCurrentUser(IJavaScriptExecutor js)
        {
            string myLogin = WindowsIdentity.GetCurrent().Name[3..];
            string myEmail = $"{myLogin}@metro-cc.ru";
            string mySid = WindowsIdentity.GetCurrent()?.User?.Value ?? "unknown";
            string? myIp = Dns.GetHostEntry(Dns.GetHostName())
                            .AddressList
                            .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?
                            .ToString();

            js.ExecuteScript($@"
                curUser.login = '{myLogin}';
                curUser.email = '{myEmail}';
                curUser.sid = '{mySid}';
                curUser.ip = '{myIp}';
                curUser.dns = '{myIp}';
            ");
        }

        [SupportedOSPlatform("windows")]
        [SuppressMessage("Performance", "CA1859", Justification = "Сохранение гибкости для разных браузеров")]
        public string GetIpFromMetroScope(string hostname)
        {
            CleanOldTempFiles();
            try
            {
                using var driver = CreateChromeDriver();
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(WAIT_TIMEOUT_SECONDS));
                driver.Navigate().GoToUrl("https://mow30lxsrv004.ru.rumadm.net/metroscope/");

                // Ждём, пока страница полностью загрузится (document.readyState === 'complete')
                wait.Until(d => d is IJavaScriptExecutor js && js.ExecuteScript("return document.readyState") is "complete");

                SetCurrentUser((IJavaScriptExecutor)driver);

                // Явно ждём, пока поле поиска станет кликабельным (т.е. видимым и включённым)
                var searchInput = wait.Until(ExpectedConditions.ElementToBeClickable(By.Id("msearch")));
                searchInput.Clear();
                searchInput.SendKeys(hostname);

                var searchBtn = wait.Until(ExpectedConditions.ElementToBeClickable(By.Id("btnSearch")));
                searchBtn.Click();

                string shortHostname = hostname.Split('.')[0].ToLower();
                // Ожидаем появления целевого элемента с IP
                var targetElement = wait.Until(ExpectedConditions.ElementIsVisible(
                    By.XPath($"//div[starts-with(@id, 'mh_') and contains(translate(@id, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), '{shortHostname}')]//span[contains(@class, 'fIP')]")));

                return targetElement.Text.Trim();
            }
            catch (WebDriverTimeoutException)
            {
                return "Не найден (таймаут)";
            }
            catch (Exception ex)
            {
                return $"Ошибка MetroScope: {ex.Message}";
            }
        }

        [SupportedOSPlatform("windows")]
        [SuppressMessage("Performance", "CA1859", Justification = "Сохранение гибкости для разных браузеров")]
        public string GetSwitchPortFromMetroScope(string ipAddress)
        {
            CleanOldTempFiles();
            try
            {
                using var driver = CreateChromeDriver();
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(WAIT_TIMEOUT_SECONDS));
                driver.Navigate().GoToUrl("https://mow30lxsrv004.ru.rumadm.net/metroscope/");

                wait.Until(d => d is IJavaScriptExecutor js && js.ExecuteScript("return document.readyState") is "complete");

                SetCurrentUser((IJavaScriptExecutor)driver);

                var searchInput = wait.Until(ExpectedConditions.ElementToBeClickable(By.Id("msearch")));
                searchInput.Clear();
                searchInput.SendKeys(ipAddress);

                var searchBtn = wait.Until(ExpectedConditions.ElementToBeClickable(By.Id("btnSearch")));
                searchBtn.Click();

                // Ожидаем элемент с заголовком "MCC"
                var targetElement = wait.Until(ExpectedConditions.ElementIsVisible(
                    By.XPath("//h3[@class='title' and contains(text(), 'MCC')]")));

                return targetElement.Text;
            }
            catch (WebDriverTimeoutException)
            {
                return "Порт не найден (таймаут)";
            }
            catch (Exception ex)
            {
                return $"Ошибка MetroScope: {ex.Message}";
            }
        }

        public void CleanOldTempFiles()
        {
            string tempPath = Path.GetTempPath();
            string[] patterns = ["chrome_chrome_BITS_*", "scoped_dir*", "chromedriver_*"];

            foreach (var pattern in patterns)
            {
                var directories = Directory.GetDirectories(tempPath, pattern);
                foreach (var dir in directories)
                {
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (Exception) { }
                }
            }
        }
    }
}