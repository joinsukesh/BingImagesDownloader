
namespace BingImagesDownloader.App_Code.Model
{
    class XMLData
    {
        public struct Nodes
        {
            /*Nodes in Status.xml*/
            public static string LastDownloadedDate = "LastDownloadedDate";

            /*Nodes in FailedDownloads.xml*/
            public static string FailedDownloads = "FailedDownloads";
            public static string Image = "Image";
            public static string URL = "URL";
            public static string Description = "Description";

            /*Nodes in Bing Image Archive XML*/
            public static string ImageElement = "image";
            public static string ImageURL = "url";
            public static string ImageDescription = "copyright";
        } 
    }
}
