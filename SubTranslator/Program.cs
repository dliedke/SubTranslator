using System;
using System.IO;
using System.Web;
using System.Text;
using System.Collections.Generic;

using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using SubtitlesParser.Classes;  // From https://github.com/AlexPoint/SubtitlesParser

namespace SubTranslator
{
    public class Program
    {
        private static TimeSpan totalTime = new();
        private static DateTime lastItemDate;

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
                    TranslateSubtitle(srtFile, originalLanguage, translatedLanguage);
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
                TranslateSubtitle(args[0], originalLanguage, translatedLanguage);
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


            foreach (var item in items)
            {
                // Translate each subtitle line
                for (int f = 0; f < item.Lines.Count; f++)
                {
                    string translatedText = TranslateText(driver, item.Lines[f], originalLanguage, translatedLanguage);
                    item.Lines[f] = translatedText;
                }
                count++;

                // Show progress and estimated time
                Console.WriteLine($"Translated subtitle {count}/{items.Count}. Estimated time to complete: " + GetEstimatedRemainingTime(count, items.Count));

                // For debugging and troubleshooting
                /*
                if (count == 3)
                {
                    break;
                }*/
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
                driver.Url = $"https://translate.google.com/?hl=pt-BR#view=home&op=translate&sl={originalLanguage}&tl={translatedLanguage}&text={HttpUtility.UrlEncode(text)}";

                // Wait a bit for translation to finish
                System.Threading.Thread.Sleep(2000);

                // Get translated text
                IWebElement element = null;
                try
                {
                    // Try to get text
                    string xPathTranslatedElementText = "//*[@jsname='W297wb']";
                    element = driver.FindElement(By.XPath(xPathTranslatedElementText));
                }
                catch
                {
                    try
                    {
                        // Try to get linked text
                        string xPathTranslatedElementLink = "//*[@jsname='jqKxS']";
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
