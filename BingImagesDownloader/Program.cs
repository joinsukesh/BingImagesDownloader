using BingImagesDownloader.App_Code.Model;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace BingImagesDownloader
{
    class Program
    {
        #region VARIABLES
        
        private static string rootDirectory = ConfigurationManager.AppSettings["RootDirectory"];
        private static string infoDirectory = ConfigurationManager.AppSettings["InfoDirectory"];
        private static string statusFilePath = ConfigurationManager.AppSettings["StatusFilePath"];
        private static string failedDownloadsFilePath = ConfigurationManager.AppSettings["FailedDownloadsFilePath"];
        private static string logsFolderPath = ConfigurationManager.AppSettings["LogsFolderPath"];
        private static string bingImagesArchiveURL = ConfigurationManager.AppSettings["BingImagesArchiveURL"];
        private static string downloadImagesFromLastDownloadDate = ConfigurationManager.AppSettings["DownloadImagesFromLastDownloadDate"];
        private static int maxImagesToDownload = Convert.ToInt32(ConfigurationManager.AppSettings["MaxImagesToDownload"]);
        private static string[] bingMarkets = ConfigurationManager.AppSettings["BingMarkets"].Split(',');
        private static string bingDomain = ConfigurationManager.AppSettings["BingDomain"];
        private static string downloadByDemand = ConfigurationManager.AppSettings["DownloadByDemand"];
        private static int downloadAttemptsForEachImage = Convert.ToInt32(ConfigurationManager.AppSettings["DownloadAttemptsForEachImage"]);
        private static string retryForFailedDownloads = ConfigurationManager.AppSettings["RetryForFailedDownloads"];
        private static string dateFormat = "yyyy-MM-dd";
        private static int totalFilesToDownload = 0;
        private static int filesDownloaded = 0;
        private static string divider = "####################################################################";

        #endregion

        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("Checking the date of last successful download...");
                int numberOfPreviousDays = GetNumberOfPreviousDays();

                if (numberOfPreviousDays >= 0)
                {
                    Console.WriteLine("Reading archive XML...");
                    List<BingImage> lstFailedDownloads = null;
                    string statusLog = string.Empty;
                    ReadArchiveData(numberOfPreviousDays, out lstFailedDownloads, out statusLog);

                    if (lstFailedDownloads != null && lstFailedDownloads.Count > 0)
                    {
                        Console.WriteLine("Saving list of failed downloads...");
                        SaveFailedDownloadsXML(lstFailedDownloads);
                    }

                    if (!string.IsNullOrEmpty(statusLog))
                    {
                        Console.WriteLine("Saving status log...");
                        SaveStatusLog(statusLog);
                    }

                    if (totalFilesToDownload > 0 && filesDownloaded > 0)
                    {
                        Console.WriteLine("Updating status in Status.xml...");
                        SaveStatusXML(DateTime.Now);
                    }

                    if (retryForFailedDownloads == "1")
                    {
                        Console.WriteLine("Re-Downloading failed downloads...");
                        RetryForFailedDownloads();
                    }
                }
                else
                    Console.WriteLine("Today's images are already downloaded.");

                Console.WriteLine("PROCESS COMPLETE");
            }
            catch (Exception ex)
            {
                //LOG EXCEPTION
                Console.WriteLine("Logging exception...");
                Environment.Exit(0);
            }
        }

        #region METHODS

        /// <summary>
        /// get the difference in days between current date and the date from which to get data from archive
        /// </summary>
        /// <returns></returns>
        private static int GetNumberOfPreviousDays()
        {
            int numberOfPreviousDays = 0;

            if (downloadByDemand != "1")
                numberOfPreviousDays = (int)(DateTime.Now.Date - GetDownloadStartDate().Date).TotalDays;
            else
                numberOfPreviousDays = Convert.ToInt32(ConfigurationManager.AppSettings["NumberOfPreviousDays"]);

            return numberOfPreviousDays;
        }

        /// <summary>
        /// read status.xml and get the download start date. This data from archive URL will be fetched from this date
        /// </summary>
        /// <param name="lastDownloadDate"></param>
        /// <param name="downloadStatus"></param>
        private static DateTime GetDownloadStartDate()
        {
            DateTime downloadStartDate = DateTime.Now;
            
            if (File.Exists(statusFilePath))
            {
                XElement xmlContent = XElement.Load(statusFilePath);

                if (xmlContent != null)
                {
                    XElement dateNode = xmlContent.DescendantsAndSelf(XMLData.Nodes.LastDownloadedDate).FirstOrDefault();
                    
                    if (dateNode != null)
                        downloadStartDate = Convert.ToDateTime(dateNode.Value).AddDays(1);                    
                }                
            }

            return downloadStartDate;
        }

        /// <summary>
        /// Create an xml which specifies the date of the last download and the download status
        /// </summary>
        private static void SaveStatusXML(DateTime dateOfLastDownload)
        {
            var xmlNode = new XElement(XMLData.Nodes.LastDownloadedDate, dateOfLastDownload.ToString(dateFormat));

            if (!Directory.Exists(infoDirectory))
                Directory.CreateDirectory(infoDirectory);

            xmlNode.Save(statusFilePath);
        }

        /// <summary>
        /// read archive xml data
        /// </summary>
        /// <param name="numberOfPreviousDays"></param>
        /// <param name="lstFailedDownloads"></param>
        /// <param name="statusLog"></param>
        private static void ReadArchiveData(int numberOfPreviousDays, out List<BingImage> lstFailedDownloads, out string statusLog)
        {
            string archiveURL = string.Empty;
            string imageURL = string.Empty;
            string imageDescription = string.Empty;
            int downloadAttemptedCount = 0;
            bool isImageDownloaded = false;
            lstFailedDownloads = new List<BingImage>();            
            statusLog = string.Empty;
            StringBuilder sbStatusLog = new StringBuilder(string.Empty);            

            if (!Directory.Exists(rootDirectory))
                Directory.CreateDirectory(rootDirectory);

            foreach (string bingMarket in bingMarkets)
            {
                XElement imagesXML = GetImagesXML(numberOfPreviousDays, bingMarket);

                if (imagesXML != null)
                {
                    IEnumerable<XElement> imageNodes = imagesXML.Descendants(XMLData.Nodes.ImageElement);

                    if (imageNodes != null && imageNodes.Count() > 0)
                    {
                        totalFilesToDownload += imageNodes.Count();
                        Console.WriteLine("Downloading images for " + bingMarket + "...");

                        foreach (XElement image in imageNodes)
                        {
                            imageURL = Convert.ToString(image.Element(XMLData.Nodes.ImageURL).Value);
                            imageDescription = Convert.ToString(image.Element(XMLData.Nodes.ImageDescription).Value);

                            if (!string.IsNullOrEmpty(imageURL))
                            {
                                isImageDownloaded = false;

                                while (downloadAttemptedCount < downloadAttemptsForEachImage)
                                {
                                    isImageDownloaded = DownloadImage(imageURL, imageDescription);

                                    if (isImageDownloaded)
                                    {
                                        filesDownloaded++;
                                        break;
                                    }
                                    else
                                        downloadAttemptedCount++;
                                }

                                if(!isImageDownloaded)
                                    lstFailedDownloads.Add(new BingImage(imageURL, imageDescription));
                            }                            
                        }
                    }
                    else
                        sbStatusLog.AppendLine(BuildStatusLog(numberOfPreviousDays, bingMarket, archiveURL, "No images XML data"));
                }
                else
                    sbStatusLog.AppendLine(BuildStatusLog(numberOfPreviousDays, bingMarket, archiveURL, "Could not load archive XML"));
            }

            statusLog = sbStatusLog.ToString();
        }
       
        /// <summary>
        /// remove invalid chars, reduce length , add time stamp and return new file name
        /// A valid file name should not exceed 260 chars.
        /// </summary>
        /// <param name="imageDescription"></param>
        /// <param name="fileExtension"></param>
        /// <returns></returns>
        private static string GetImageFileName(string imageURL, string imageDescription)
        {
            string newFileName = imageDescription;
            newFileName = Regex.Replace(newFileName, @"[^\u0000-\u007F]", string.Empty); //remove non-ascii characters
            char[] invalidFileNameChars = Path.GetInvalidFileNameChars();

            foreach (char c in invalidFileNameChars)
                newFileName = newFileName.Replace(c.ToString(), "");

            if (newFileName.Length > 230)
                newFileName = newFileName.Substring(0, 230);

            //newFileName = newFileName + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + Path.GetExtension(imageURL);
            return newFileName;
        }

        /// <summary>
        /// the image url may sometimes be incorrect having a url like /th/xyz.jpg&ashjg.jpg
        /// This function will correct it to /th/xyz.jpg
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        private static string GetCorrectedURL(string imageURL)
        {
            string stopAt = "";

            if (!String.IsNullOrWhiteSpace(imageURL))
            {
                if(imageURL.Contains(".jpg"))
                {
                    stopAt = ".jpg";
                }
                else
                {
                    if (imageURL.Contains(".png"))
                    {
                        stopAt = ".png";
                    }
                }

                int charLocation = imageURL.IndexOf(stopAt, StringComparison.Ordinal);

                if (charLocation > 0)
                {
                    imageURL = imageURL.Substring(0, charLocation) + stopAt;
                    return imageURL;
                }
            }

            return String.Empty;
        }

        /// <summary>
        /// download image from URL and save it in root dir. The file name will be the image description
        /// </summary>
        /// <param name="imageURL"></param>
        /// <param name="imageDescription"></param>
        private static bool DownloadImage(string imageURL, string imageDescription)
        {
            bool isDownloaded = false;            

            try
            {
                imageURL = GetCorrectedURL(imageURL);

                string extension = "";

                if (imageURL.Contains(".jpg"))
                {
                    extension = ".jpg";
                }
                else
                {
                    if (imageURL.Contains(".png"))
                    {
                        extension = ".png";
                    }
                }

                using (WebClient webClient = new WebClient())
                {
                    string fileName = GetImageFileName("", imageDescription) + extension;
                    fileName = rootDirectory + "\\" + fileName;

                    if (!File.Exists(fileName))
                    {
                        webClient.DownloadFile(bingDomain + "/" + imageURL, fileName);
                        isDownloaded = true;
                    }
                    else
                    {
                        isDownloaded = true;
                    }
                }
            }
            catch (Exception ex){}

            return isDownloaded;
        }

        /// <summary>
        /// build text of status log
        /// </summary>
        /// <param name="numberOfPreviousDays"></param>
        /// <param name="bingMarket"></param>
        /// <param name="archiveURL"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        private static string BuildStatusLog(int numberOfPreviousDays, string bingMarket, string archiveURL, string message)
        {
            StringBuilder statusLog = new StringBuilder(string.Empty);
            statusLog.AppendLine(divider);
            statusLog.AppendLine("DATE OF EXECUTION       : " + DateTime.Now.ToString(dateFormat + " HH:mm:ss"));
            statusLog.AppendLine("NO. OF PREVIOUS DAYS    : " + numberOfPreviousDays);
            statusLog.AppendLine("MAX. IMAGES TO DOWNLOAD : " + maxImagesToDownload);
            statusLog.AppendLine("BING MARKET             : " + bingMarket);
            statusLog.AppendLine("ARCHIVE DATA URL        : " + archiveURL);
            statusLog.AppendLine("STATUS                  : " + message);
            return statusLog.ToString();
        }

        /// <summary>
        /// save list of images that could not be downloaded
        /// </summary>
        /// <param name="lstFailedDownloads"></param>
        private static void SaveFailedDownloadsXML(List<BingImage> lstFailedDownloads)
        {
            XmlDocument xDoc = new XmlDocument();

            if (!File.Exists(failedDownloadsFilePath))
            {
                XmlElement rootNode = xDoc.CreateElement(XMLData.Nodes.FailedDownloads);
                xDoc.AppendChild(rootNode);

                if (!Directory.Exists(infoDirectory))
                    Directory.CreateDirectory(infoDirectory);

                xDoc.Save(failedDownloadsFilePath);
            }
            
            xDoc.Load(failedDownloadsFilePath);

            foreach (BingImage bingImage in lstFailedDownloads)
            {
                XmlElement imageNode = xDoc.CreateElement(XMLData.Nodes.Image);

                XmlElement urlNode = xDoc.CreateElement(XMLData.Nodes.URL);
                urlNode.InnerText = bingImage.ImageURL;
                imageNode.AppendChild(urlNode);

                XmlElement descriptionNode = xDoc.CreateElement(XMLData.Nodes.Description);
                descriptionNode.InnerText = bingImage.ImageDescription;
                imageNode.AppendChild(descriptionNode);

                xDoc.DocumentElement.AppendChild(imageNode);
            }

            xDoc.Save(failedDownloadsFilePath);
        }

        /// <summary>
        /// save status log
        /// </summary>
        /// <param name="message"></param>
        private static void SaveStatusLog(string message)
        {
            string todaysFileName = logsFolderPath + "\\Status_" + DateTime.Now.ToString(dateFormat) + ".txt";

            if (!Directory.Exists(logsFolderPath))
                Directory.CreateDirectory(logsFolderPath);

            if (File.Exists(todaysFileName))
            {
                using (StreamWriter sw = File.AppendText(todaysFileName))
                {
                    sw.WriteLine(message);
                }
            }
            else
                File.WriteAllText(todaysFileName, message);
        }

        /// <summary>
        /// read faileddownloads.xml and try downloading them again
        /// </summary>
        private static void RetryForFailedDownloads()
        {
            if(File.Exists(failedDownloadsFilePath))
            {
                IEnumerable<XElement> files = XElement.Load(failedDownloadsFilePath).Descendants(XMLData.Nodes.Image);

                if (files != null && files.Count() > 0)
                {
                    bool isImageDownloaded = false;
                    int downloadAttemptedCount = 0;                    
                    List<BingImage> lstFailedDownloads = new List<BingImage>();
                    string imageURL = string.Empty;
                    string imageDescription = string.Empty;

                    //download the files in the faileddownloads.xml
                    foreach(XElement file in files)
                    {
                        imageURL = Convert.ToString(file.Element(XMLData.Nodes.ImageURL).Value);
                        imageDescription = Convert.ToString(file.Element(XMLData.Nodes.ImageDescription).Value);

                        if(!string.IsNullOrEmpty(imageURL))
                        {
                            while (downloadAttemptedCount < downloadAttemptsForEachImage)
                            {
                                isImageDownloaded = DownloadImage(imageURL, imageDescription);

                                if (isImageDownloaded)
                                {
                                    filesDownloaded++;
                                    break;
                                }
                                else
                                    downloadAttemptedCount++;
                            }

                            if (!isImageDownloaded)
                                lstFailedDownloads.Add(new BingImage(imageURL, imageDescription));
                        }
                    }

                    //replace the old file with the new list
                    XmlDocument xDoc = new XmlDocument();
                    XmlElement rootNode = xDoc.CreateElement(XMLData.Nodes.FailedDownloads);
                    xDoc.AppendChild(rootNode);

                    foreach (BingImage bingImage in lstFailedDownloads)
                    {
                        XmlElement imageNode = xDoc.CreateElement(XMLData.Nodes.Image);

                        XmlElement urlNode = xDoc.CreateElement(XMLData.Nodes.URL);
                        urlNode.InnerText = bingImage.ImageURL;
                        imageNode.AppendChild(urlNode);

                        XmlElement descriptionNode = xDoc.CreateElement(XMLData.Nodes.Description);
                        descriptionNode.InnerText = bingImage.ImageDescription;
                        imageNode.AppendChild(descriptionNode);

                        xDoc.DocumentElement.AppendChild(imageNode);
                    }

                    xDoc.Save(failedDownloadsFilePath);
                }
            }
        }

        /// <summary>
        /// This function returns the bing images XML data
        /// 
        /// </summary>
        /// <param name="numberOfPreviousDays"></param>
        /// <param name="bingMarket"></param>
        /// <returns></returns>
        private static XElement GetImagesXML(int numberOfPreviousDays, string bingMarket)
        {
            XElement imagesXML = null;            

            if (downloadByDemand == "1")
            {
                while(imagesXML == null && numberOfPreviousDays >= 0)
                {
                    imagesXML = VerifyImagesXML(numberOfPreviousDays, bingMarket);

                    if (imagesXML != null)
                        break;
                    else
                        numberOfPreviousDays--;
                }
            }
            else
                imagesXML = XElement.Load(string.Format(bingImagesArchiveURL, numberOfPreviousDays, maxImagesToDownload, bingMarket));

            return imagesXML;
        }

        /// <summary>
        /// When downloading by demand and say the no. of previous days is 10, then there may be no xml data for it in a certain bing market.
        /// This function will check repeatedly, by reducing the no. of previous days
        /// </summary>
        /// <param name="numberOfPreviousDays"></param>
        /// <param name="bingMarket"></param>
        /// <returns></returns>
        private static XElement VerifyImagesXML(int numberOfPreviousDays, string bingMarket)
        {
            try
            {
                string archiveURL = string.Format(bingImagesArchiveURL, numberOfPreviousDays, maxImagesToDownload, bingMarket);
                return XElement.Load(string.Format(bingImagesArchiveURL, numberOfPreviousDays, maxImagesToDownload, bingMarket));
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}
