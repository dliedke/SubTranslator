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
        private static TimeSpan totalTime = new TimeSpan();
        private static DateTime lastItemDate;

        static void Main(string[] args)
        {
            // Check if subtitle file was provided
            if (args.Length < 1)
            {
                Console.WriteLine("Please provide the english subtitle file!");
                return;
            }

            // Check if subtitle file exists
            if (args.Length > 0 && string.IsNullOrEmpty(args[0])==false && File.Exists(args[0])==false)
            {
                Console.WriteLine("Please provide an english subtitle file that exists!");
                return;
            }

            // Initial file to be translated
            string file = args[0];
            string originalLanguage = "en";

            // Final translated file
            string translatedLanguage = "pt";
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
            IWebDriver driver = new ChromeDriver();
            int count = 0;
            lastItemDate = DateTime.Now;

            foreach (var item in items)
            {
                for(int f=0;f<item.Lines.Count;f++)
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

            // Write the subtitle translated to disk
            using StreamWriter sw = File.CreateText(translatedFile);
            int index = 1;
            foreach (var item in items)
            {
                sw.Write(GetStringSubtitleItem(index, item));
                index++;
            }
            sw.Close();
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
                var element = driver.FindElement(By.XPath("//span[@class='tlid-translation translation']"));

                // Translation might not have ended, so retry 3 times
                if (element.Text.EndsWith("..."))
                {
                    System.Threading.Thread.Sleep(3000);
                    element = driver.FindElement(By.XPath("//span[@class='tlid-translation translation']"));
                }
                if (element.Text.EndsWith("..."))
                {
                    System.Threading.Thread.Sleep(3000);
                    element = driver.FindElement(By.XPath("//span[@class='tlid-translation translation']"));
                }
                if (element.Text.EndsWith("..."))
                {
                    System.Threading.Thread.Sleep(3000);
                    element = driver.FindElement(By.XPath("//span[@class='tlid-translation translation']"));
                }
                return element.Text;
            }
            catch
            {
                // In case of any exception retry 3 times before setting error
                retryCount++;
                if (retryCount == 3)
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
