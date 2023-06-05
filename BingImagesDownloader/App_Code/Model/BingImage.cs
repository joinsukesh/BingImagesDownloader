
namespace BingImagesDownloader.App_Code.Model
{
    class BingImage
    {
        public string ImageURL { get; set; }
        public string ImageDescription { get; set; }

        public BingImage(string imageURL, string imageDescription)
        {
            ImageURL = imageURL;
            ImageDescription = imageDescription;
        }
    }
}
