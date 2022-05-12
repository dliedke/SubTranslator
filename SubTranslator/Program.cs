using System;
using System.IO;
using System.Web;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using SubtitlesParser.Classes;  // From https://github.com/AlexPoint/SubtitlesParser
using WebDriverManager;
using WebDriverManager.DriverConfigs.Impl;

namespace SubTranslator
{
    public class Program
    {
        private static TimeSpan totalTime = new();
        private static DateTime lastItemDate;
        private static readonly Stopwatch timer = new();
        private static int currentIndexSubtitle = 1;

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

            // Close any existing ChromeDriver processes
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
                    Console.WriteLine("Please provide a directory with at least one .srt file!");
                    return;
                }

                // Translate multiple .srt files
                foreach (string srtFile in srtFileList)
                {
                    currentIndexSubtitle = 1;
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

            // Read the original subtitle and parse it
            List<SubtitleItem> items;
            var parser = new SubtitlesParser.Classes.Parsers.SrtParser();
            using (var fileStream = File.OpenRead(file))
            {
                items = parser.ParseStream(fileStream, Encoding.UTF8);
            }

            // Check if we already have a translated file in progress
            // Read, parse it and merge with the original subtitle file
            int totalItemsAlreadyTranslated = 0;
            if (File.Exists(translatedFile))
            {
                List<SubtitleItem> itemsAlreadyTranslated;
                using var fileStream = File.OpenRead(translatedFile);
                itemsAlreadyTranslated = parser.ParseStream(fileStream, Encoding.UTF8);
                items.RemoveRange(0,itemsAlreadyTranslated.Count);
                items.InsertRange(0,itemsAlreadyTranslated);
                currentIndexSubtitle = itemsAlreadyTranslated.Count + 1;
                totalItemsAlreadyTranslated = itemsAlreadyTranslated.Count;

                Console.WriteLine($"NOTE: Found existing translation in progress. Resuming from {currentIndexSubtitle}/{items.Count}...");
            }

            // Download latest Chromedriver automatically
            // Open source project from https://github.com/rosolko/WebDriverManager.Net
            new DriverManager().SetUpDriver(new ChromeConfig());

            // Translate the subtitle with Google Translator and Selenium
            // (whole page translation has severe issues in the translation)
            ChromeDriverService service = ChromeDriverService.CreateDefaultService();
            service.SuppressInitialDiagnosticInformation = true;  // Disable logs
            service.EnableVerboseLogging = false;                 // Disable logs
            service.EnableAppendLog = false;                      // Disable logs
            service.HideCommandPromptWindow = true;               // Hide any ChromeDriver window
            IWebDriver driver = new ChromeDriver(service);
            lastItemDate = DateTime.Now;

            // Sometimes first URL is not correctly loaded so just load google.com
            driver.Url = "http://www.google.com";

            // Merge the multilines subtitle item into single one
            // when it is not starting with a hiphen
            // (movie talk between people should still be multilines)
            foreach (var item in items)
            {
                if (item.Lines.Count > 1 &&
                    !string.IsNullOrEmpty(item.Lines[0]) &&
                    !item.Lines[0].Trim().StartsWith("-"))
                {
                    item.Lines = new List<string> { String.Join('|', item.Lines) };
                }
            }

            // Translate all subtitle items not yet translated
            var itemsToBeTranslated = items.GetRange(currentIndexSubtitle - 1, items.Count - currentIndexSubtitle + 1);
            foreach (var item in itemsToBeTranslated)
            {
                // Translate each subtitle line
                for (int f = 0; f < item.Lines.Count; f++)
                {
                    string translatedText = TranslateText(driver, item.Lines[f], originalLanguage, translatedLanguage);
                    translatedText = translatedText.Replace(" | ", "\r\n");  // reconstruct the new lines
                    item.Lines[f] = translatedText;
                }
                
                // Show progress, total processing time and estimated time to finish
                TimeSpan timeSpan = timer.Elapsed;
                string totalProcessingTime = string.Format("{0:D2}:{1:D2}:{2:D2}", timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds);
                decimal percent = Math.Round(((decimal) currentIndexSubtitle / itemsToBeTranslated.Count * 100), 2);
                Console.WriteLine($"{totalProcessingTime} - Translated subtitle {currentIndexSubtitle}/{itemsToBeTranslated.Count} ({percent} %). Estimated time to complete: " + GetEstimatedRemainingTime(currentIndexSubtitle-totalItemsAlreadyTranslated, itemsToBeTranslated.Count));

                // Creates the new translated subtitle file so we can resume 
                // later automatically if things go wrong in this long process
                CreateNewSubtileFile(translatedFile, items);

                // Update current subtitle index
                currentIndexSubtitle++;

                // Every 10 translations wait for 5 seconds
                if (currentIndexSubtitle % 10 == 0)
                {
                    // Wait a bit to avoid being blocked by Google
                    Console.Write("Waiting 5s....");
                    System.Threading.Thread.Sleep(5000);
                    Console.WriteLine("Done.");
                }
            }

            // Close and quit ChromeDriver process
            driver.Close();
            driver.Quit();

            // Show file name that was sucessfully translated
            Console.WriteLine($"DONE - Translated subtitle file: \"{Path.GetFileName(file)}\"\r\n");
        }

        private static void CreateNewSubtileFile(string translatedFile, List<SubtitleItem> items)
        {
            // Creates the new translated subtitle file
            StreamWriter sw = File.CreateText(translatedFile);
            int index = 1;
            foreach (var item in items)
            {
                sw.Write(GetStringSubtitleItem(index, item));
                index++;

                if (index > currentIndexSubtitle)
                {
                    break;
                }
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
                driver.Url = $"https://translate.google.com/?hl=en-US&op=translate&sl={originalLanguage}&tl={translatedLanguage}&text={HttpUtility.UrlEncode(text)}";

                // Get translated text
                ReadOnlyCollection<IWebElement> elements = null;

                try
                {
                    // Try to get the translated text
                    string xPathTranslatedElementText = "//*[@jsname='W297wb']";
                    WebDriverExtensions.WaitExtension.WaitUntilElement(driver, By.XPath(xPathTranslatedElementText), 10);
                    elements = driver.FindElements(By.XPath(xPathTranslatedElementText));
                }
                catch
                {
                    try
                    {
                        // Try to get translated linked text
                        string xPathTranslatedElementLink = "//*[@jsname='jqKxS']";
                        WebDriverExtensions.WaitExtension.WaitUntilElement(driver, By.XPath(xPathTranslatedElementLink), 10);
                        elements = driver.FindElements(By.XPath(xPathTranslatedElementLink));
                    }
                    catch
                    { }
                }

                StringBuilder fullTranslatedText = new();

                // If we have a translation with both genders get only one
                if (driver.PageSource.Contains("Translations are gender-specific"))
                {
                    fullTranslatedText.Append(elements[elements.Count > 1 ? 1 : 0].Text);
                }
                else
                {
                    // Concatenate all elements 
                    foreach (var element in elements)
                    {
                        fullTranslatedText.Append(element.Text + " ");
                    }
                }

                return fullTranslatedText.ToString().Trim();
            }
            catch 
            {
                // In case of any exception retry 5 times before setting error
                // for this specific subtitle
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
                                    string.Join(Environment.NewLine, item.Lines)
                                          .Replace("|","\r\n")
                                          .Replace("</ i>", "</i>")
                                          .Replace("</ b>", "</b>"));

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
            if (currentItem < 11)
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
