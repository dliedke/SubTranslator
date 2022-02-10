using System;
using System.IO;
using System.Web;
using System.Text;
using System.Collections.Generic;

using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using SubtitlesParser.Classes;  // From https://github.com/AlexPoint/SubtitlesParser
using System.Diagnostics;

namespace SubTranslator
{
    public class Program
    {
        private static TimeSpan totalTime = new();
        private static DateTime lastItemDate;
        private static Stopwatch timer = new();

        static void Main(string[] args)
        {
            // Original subtitle language
            string originalLanguage = "en";

            // Final language for subtitle translation
            string translatedLanguage = "pt";

            // Check if subtitle file/directory was provided
            if (args.Length < 1)
            {
                Console.WriteLine("Please provide the english subtitle file or a directory with .srt files!");
                return;
            }

            // Close any existing ChromeDriver process
            Console.WriteLine("Closing any existing ChromeDriver process");
            Process.Start("taskkill", "/F /IM chromedriver.exe /T");

            // Check if provided args is a directory
            if (Directory.Exists(args[0]))
            {
                // Find all srt files in the directory
                string[] srtFileList = Directory.GetFiles(args[0], "*.srt");

                // If not srt file was found
                if (srtFileList.Length == 0)
                {
                    Console.WriteLine("Please provide a directory with .srt files!");
                    return;
                }

                // Translate multiple .srt files
                foreach (string srtFile in srtFileList)
                {
                    timer.Restart();
                    TranslateSubtitle(srtFile, originalLanguage, translatedLanguage);
                    timer.Stop();
                }

                return;
            }

            // Check if subtitle file exists
            if (args.Length > 0 && string.IsNullOrEmpty(args[0]) == false && File.Exists(args[0]) == false)
            {
                Console.WriteLine("Please provide an english subtitle file that exists!");
                return;
            }

            // Translate a single .srt file
            if (File.Exists(args[0]))
            {
                timer.Restart();
                TranslateSubtitle(args[0], originalLanguage, translatedLanguage);
                timer.Stop();
            }
        }

        private static void TranslateSubtitle(string file, string originalLanguage, string translatedLanguage)
        {
            // Show file name being translated
            Console.WriteLine($"\r\nTranslating subtitle file: \"{Path.GetFileName(file)}\"");

            // Final translated file
            string translatedFile = Path.Combine(Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(file) + $"-{translatedLanguage}.srt");

            // Read the subtitle and parse it
            List<SubtitleItem> items;
            var parser = new SubtitlesParser.Classes.Parsers.SrtParser();
            using (var fileStream = File.OpenRead(file))
            {
                items = parser.ParseStream(fileStream, Encoding.UTF8);
            }

            // Translate the subtitle with Google Translator and Selenium
            // (page translation has severe issues in the translation)
            ChromeDriverService service = ChromeDriverService.CreateDefaultService();
            service.SuppressInitialDiagnosticInformation = true;  // Disable logs
            service.EnableVerboseLogging = false;                 // Disable logs
            service.EnableAppendLog = false;                      // Disable logs
            service.HideCommandPromptWindow = true;
            IWebDriver driver = new ChromeDriver(service);
            int count = 0;
            lastItemDate = DateTime.Now;

            // Merge the multilines subtitle item into single one
            // when it is not starting with a hiphen
            // (talk between people should still be multilines)
            foreach (var item in items)
            {
                if (item.Lines.Count > 1 &&
                    !string.IsNullOrEmpty(item.Lines[0]) &&
                    !item.Lines[0].Trim().StartsWith("-"))
                {
                    item.Lines = new List<string> { String.Join('|', item.Lines) };
                }
            }

            // Translate all subtitle items
            foreach (var item in items)
            {
                // Translate each subtitle line
                for (int f = 0; f < item.Lines.Count; f++)
                {
                    string translatedText = TranslateText(driver, item.Lines[f], originalLanguage, translatedLanguage);
                    translatedText = translatedText.Replace(" | ", "\r\n");
                    item.Lines[f] = translatedText;
                }
                count++;

                if (count % 10 == 0)
                {
                    // Wait a bit for to avoid being blocked
                    Console.Write("Waiting 5s....");
                    System.Threading.Thread.Sleep(5000);
                    Console.WriteLine("Done.");
                }

                // Show progress and estimated time
                TimeSpan timeSpan = timer.Elapsed;
                string totalProcessingTime = string.Format("{0:D2}:{1:D2}:{2:D2}", timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds);
                Console.WriteLine($"{totalProcessingTime} - Translated subtitle {count}/{items.Count}. Estimated time to complete: " + GetEstimatedRemainingTime(count, items.Count));
            }

            driver.Close();
            driver.Quit();

            // Creates the new subtitle file
            StreamWriter sw = File.CreateText(translatedFile);
            int index = 1;
            foreach (var item in items)
            {
                sw.Write(GetStringSubtitleItem(index, item));
                index++;
            }
            sw.Close();

            // Show file name that was translated
            Console.WriteLine($"DONE - Translated subtitle file: \"{Path.GetFileName(file)}\"\r\n");
        }

        private static string TranslateText(IWebDriver driver, string text, string originalLanguage, string translatedLanguage)
        {
            int retryCount = 0;

        retry:

            try
            {
                // Translate the text with Google Translator
                driver.Url = $"https://translate.google.com/?hl=pt-BR&op=translate&sl={originalLanguage}&tl={translatedLanguage}&text={HttpUtility.UrlEncode(text)}";
                
                // Get translated text
                IWebElement element = null;
                try
                {
                    // Try to get text
                    string xPathTranslatedElementText = "//*[@jsname='W297wb']";
                    WebDriverExtensions.WaitExtension.WaitUntilElement(driver, By.XPath(xPathTranslatedElementText), 5);
                    element = driver.FindElement(By.XPath(xPathTranslatedElementText));
                }
                catch
                {
                    try
                    {
                        // Try to get linked text
                        string xPathTranslatedElementLink = "//*[@jsname='jqKxS']";
                        WebDriverExtensions.WaitExtension.WaitUntilElement(driver, By.XPath(xPathTranslatedElementLink), 5);
                        element = driver.FindElement(By.XPath(xPathTranslatedElementLink));
                    }
                    catch
                    { }
                }

                return element.Text;
            }
            catch 
            {
                // In case of any exception retry 5 times before setting error
                retryCount++;
                if (retryCount == 5)
                {
                    return "ERROR";
                }
            }
 
            goto retry;
        }

        private static string GetStringSubtitleItem(int index, SubtitleItem item)
        {
            /* Generate subtitle in srt format for single item, example:
             * 
             * 1575
               02:25:02,230 --> 02:25:05,665
               And he thought
               you were just finer than frog fur.
            */

            var startTs = new TimeSpan(0, 0, 0, 0, item.StartTime);
            var endTs = new TimeSpan(0, 0, 0, 0, item.EndTime);

            var subText = string.Format("{0}\r\n{1} --> {2}\r\n{3}\r\n\r\n",
                                    index,
                                    startTs.ToString(@"hh\:mm\:ss\,fff"),
                                    endTs.ToString(@"hh\:mm\:ss\,fff"),
                                    string.Join(Environment.NewLine, item.Lines));

            return subText;
        }

        private static string GetEstimatedRemainingTime(int currentItem, int totalItems)
        {
            // Get time since last call
            TimeSpan lastItemTime = DateTime.Now - lastItemDate;

            // Increase total time
            totalTime += lastItemTime;

            // Get estimated time
            TimeSpan estimatedRemainingTime = (totalTime / currentItem) * (totalItems - currentItem);

            // Update last call time
            lastItemDate = DateTime.Now;

            // Not enough data to show estimated time
            if (currentItem < 10)
            {
                return "Please wait...";
            }
            else
            {
                // Show estimated time in hours, minutes and seconds
                return $"{estimatedRemainingTime.Hours}h {estimatedRemainingTime.Minutes}m {estimatedRemainingTime.Seconds}s";
            }
        }
    }
}
