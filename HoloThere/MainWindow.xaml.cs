using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ProjectOxford.Common.Contract;
using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Contract;

namespace FaceTutorial
{
    public partial class MainWindow : Window
    {
        // Replace the first parameter with your valid subscription key.
        //
        // Replace or verify the region in the second parameter.
        //
        // You must use the same region in your REST API call as you used to obtain your subscription keys.
        // For example, if you obtained your subscription keys from the westus region, replace
        // "westcentralus" in the URI below with "westus".
        //
        // NOTE: Free trial subscription keys are generated in the westcentralus region, so if you are using
        // a free trial subscription key, you should not need to change this region.

        //private string textFilePath = @"C:\Users\t-dima\Documents\Visual Studio 2017\Projects\HoloThere\FaceLists.txt";
        private string textFilePath = @"C:\Users\t-saji\Documents\HoloThere\FaceLists.txt";

        private readonly IFaceServiceClient faceServiceClient =
            new FaceServiceClient("18fd70f226404a5faaa15f1541d5b94f", "https://westcentralus.api.cognitive.microsoft.com/face/v1.0");

        Face[] faces;                   // The list of detected faces.
        String[] faceDescriptions;      // The list of descriptions for the detected faces.
        double resizeFactor;            // The resize factor for the displayed image.

        public MainWindow()
        {
            InitializeComponent();
        }

        // Displays the image and calls Detect Faces.
        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            // Load faceList to the cloud
            // await WriteFaceList();

            // Get the image file to scan from the user.
            var openDlg = new Microsoft.Win32.OpenFileDialog();
            
            openDlg.Filter = "JPEG Image(*.jpg)|*.jpg";
            bool? result = openDlg.ShowDialog(this);

            // Return if canceled.
            if (!(bool)result)
            {
                return;
            }

            // Display the image file.
            string filePath = openDlg.FileName;

            Uri fileUri = new Uri(filePath);
            BitmapImage bitmapSource = new BitmapImage();

            bitmapSource.BeginInit();
            bitmapSource.CacheOption = BitmapCacheOption.None;
            bitmapSource.UriSource = fileUri;
            bitmapSource.EndInit();

            FacePhoto.Source = bitmapSource;

            // Detect any faces in the image.
            Title = "Detecting...";
            faces = await UploadAndDetectFaces(filePath);
            Title = String.Format("Detection Finished. {0} face(s) detected", faces.Length);

            if (faces.Length > 0)
            {
                // Prepare to draw rectangles around the faces.
                DrawingVisual visual = new DrawingVisual();
                DrawingContext drawingContext = visual.RenderOpen();
                drawingContext.DrawImage(bitmapSource,
                    new Rect(0, 0, bitmapSource.Width, bitmapSource.Height));
                double dpi = bitmapSource.DpiX;
                resizeFactor = 96 / dpi;
                faceDescriptions = new String[faces.Length];

                Dictionary<string, string> faceDict = GetFaceDict();

                for (int i = 0; i < faces.Length; ++i)
                {
                    Face face = faces[i];

                    // Draw a rectangle on the face.
                    drawingContext.DrawRectangle(
                        Brushes.Transparent,
                        new Pen(Brushes.Red, 2),
                        new Rect(
                            face.FaceRectangle.Left * resizeFactor,
                            face.FaceRectangle.Top * resizeFactor,
                            face.FaceRectangle.Width * resizeFactor,
                            face.FaceRectangle.Height * resizeFactor
                            )
                    );
                    
                    // Store the face description.
                    faceDescriptions[i] = FaceDescription(face);
                    
                    // For each face, get similar faces to try and come up with that person's name
                    string person = await this.GetSimilarFaces(face, faceDict);
                    if (person != null)
                    {
                        drawingContext.DrawText(new FormattedText(person, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                            new Typeface("Helvetica"), 100.0, new SolidColorBrush(Color.FromRgb(0, 0, 0))),
                            new Point(face.FaceRectangle.Left * resizeFactor,
                            face.FaceRectangle.Top * resizeFactor));
                    }
                }

                drawingContext.Close();

                // Display the image with the rectangle around the face.
                RenderTargetBitmap faceWithRectBitmap = new RenderTargetBitmap(
                    (int)(bitmapSource.PixelWidth * resizeFactor),
                    (int)(bitmapSource.PixelHeight * resizeFactor),
                    96,
                    96,
                    PixelFormats.Pbgra32);

                faceWithRectBitmap.Render(visual);
                FacePhoto.Source = faceWithRectBitmap;

                // Set the status bar text.
                //faceDescriptionStatusBar.Text = "Place the mouse pointer over a face to see the face description.";
            }
        }

        // Displays the face description when the mouse is over a face rectangle.

        private void FacePhoto_MouseMove(object sender, MouseEventArgs e)
        {
            // If the REST call has not completed, return from this method.
            if (faces == null)
                return;

            // Find the mouse position relative to the image.
            Point mouseXY = e.GetPosition(FacePhoto);

            ImageSource imageSource = FacePhoto.Source;
            BitmapSource bitmapSource = (BitmapSource)imageSource;

            // Scale adjustment between the actual size and displayed size.
            var scale = FacePhoto.ActualWidth / (bitmapSource.PixelWidth / resizeFactor);

            // Check if this mouse position is over a face rectangle.
            //bool mouseOverFace = false;

            for (int i = 0; i < faces.Length; ++i)
            {
                FaceRectangle fr = faces[i].FaceRectangle;
                double left = fr.Left * scale;
                double top = fr.Top * scale;
                double width = fr.Width * scale;
                double height = fr.Height * scale;

                // Display the face description for this face if the mouse is over this face rectangle.
                if (mouseXY.X >= left && mouseXY.X <= left + width && mouseXY.Y >= top && mouseXY.Y <= top + height)
                {
                    //faceDescriptionStatusBar.Text = faceDescriptions[i];
                    //mouseOverFace = true;
                    break;
                }
            }

            // If the mouse is not over a face rectangle.
            //if (!mouseOverFace)
                //faceDescriptionStatusBar.Text = "Place the mouse pointer over a face to see the face description.";
        }

        // Uploads the image file and calls Detect Faces.
        private async Task<Face[]> UploadAndDetectFaces(string imageFilePath)
        {
            // The list of Face attributes to return.
            IEnumerable<FaceAttributeType> faceAttributes =
                new FaceAttributeType[] { FaceAttributeType.Gender, FaceAttributeType.Age, FaceAttributeType.Smile, FaceAttributeType.Emotion, FaceAttributeType.Glasses, FaceAttributeType.Hair };

            // Call the Face API.
            try
            {
                using (Stream imageFileStream = File.OpenRead(imageFilePath))
                {
                    Face[] faces = await faceServiceClient.DetectAsync(imageFileStream, returnFaceId: true, returnFaceLandmarks: false, returnFaceAttributes: faceAttributes);
                    return faces;
                }
            }
            // Catch and display Face API errors.
            catch (FaceAPIException f)
            {
                MessageBox.Show(f.ErrorMessage, f.ErrorCode);
                return new Face[0];
            }
            // Catch and display all other errors.
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Error");
                return new Face[0];
            }
        }

        // Returns a string that describes the given face.
        private string FaceDescription(Face face)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("Face: ");

            // Add the gender, age, and smile.
            sb.Append(face.FaceAttributes.Gender);
            sb.Append(", ");
            sb.Append(face.FaceAttributes.Age);
            sb.Append(", ");
            sb.Append(String.Format("smile {0:F1}%, ", face.FaceAttributes.Smile * 100));

            // Add the emotions. Display all emotions over 10%.
            sb.Append("Emotion: ");
            EmotionScores emotionScores = face.FaceAttributes.Emotion;
            if (emotionScores.Anger >= 0.1f) sb.Append(String.Format("anger {0:F1}%, ", emotionScores.Anger * 100));
            if (emotionScores.Contempt >= 0.1f) sb.Append(String.Format("contempt {0:F1}%, ", emotionScores.Contempt * 100));
            if (emotionScores.Disgust >= 0.1f) sb.Append(String.Format("disgust {0:F1}%, ", emotionScores.Disgust * 100));
            if (emotionScores.Fear >= 0.1f) sb.Append(String.Format("fear {0:F1}%, ", emotionScores.Fear * 100));
            if (emotionScores.Happiness >= 0.1f) sb.Append(String.Format("happiness {0:F1}%, ", emotionScores.Happiness * 100));
            if (emotionScores.Neutral >= 0.1f) sb.Append(String.Format("neutral {0:F1}%, ", emotionScores.Neutral * 100));
            if (emotionScores.Sadness >= 0.1f) sb.Append(String.Format("sadness {0:F1}%, ", emotionScores.Sadness * 100));
            if (emotionScores.Surprise >= 0.1f) sb.Append(String.Format("surprise {0:F1}%, ", emotionScores.Surprise * 100));

            // Add glasses.
            sb.Append(face.FaceAttributes.Glasses);
            sb.Append(", ");

            // Add hair.
            sb.Append("Hair: ");

            // Display baldness confidence if over 1%.
            if (face.FaceAttributes.Hair.Bald >= 0.01f)
                sb.Append(String.Format("bald {0:F1}% ", face.FaceAttributes.Hair.Bald * 100));

            // Display all hair color attributes over 10%.
            HairColor[] hairColors = face.FaceAttributes.Hair.HairColor;
            foreach (HairColor hairColor in hairColors)
            {
                if (hairColor.Confidence >= 0.1f)
                {
                    sb.Append(hairColor.Color.ToString());
                    sb.Append(String.Format(" {0:F1}% ", hairColor.Confidence * 100));
                }
            }

            // Return the built string.
            return sb.ToString();
        }

        private async Task WriteFaceList()
        {
            Dictionary<string, string[]> faceDict = new Dictionary<string, string[]>();

            string[] diegoList = new string[]
            {
                "https://scontent-dft4-1.xx.fbcdn.net/v/t31.0-8/13502878_10154437811366162_7488307966184526219_o.jpg?oh=7d097e08112ee7abae30225130ab71d1&oe=59F2FC2F",
                "https://scontent-dft4-1.xx.fbcdn.net/v/t1.0-9/13516363_10154453750726162_3133429171807587927_n.jpg?oh=c6a02666c1686016d7f40663e2872124&oe=5A01B945",
                "https://scontent-dft4-1.xx.fbcdn.net/v/t1.0-9/18527595_10212987359224548_5875913060566074611_n.jpg?oh=27fdb22488862553c8812d5f318335ef&oe=5A039F04",
                "https://scontent-dft4-1.xx.fbcdn.net/v/t31.0-8/20157777_10155782622261162_5557152701187816603_o.jpg?oh=63ba3dcd789a5e373c4c439fa337ac8e&oe=59ECB619"
            };

            faceDict.Add("Diego", diegoList);

            string[] treyList = new string[]
            {
                "https://scontent-sea1-1.xx.fbcdn.net/v/t1.0-9/10881643_851978501544396_603724560499557519_n.jpg?oh=d34465894d779cf26ea30bf4c585bc0b&oe=5A36385C",
                "https://scontent-sea1-1.xx.fbcdn.net/v/t1.0-9/12495168_1049133575162220_7291081008360702154_n.jpg?oh=77d082e74a06ba49c6536f43e147dea5&oe=59F81055",
                "https://scontent-sea1-1.xx.fbcdn.net/v/t1.0-9/13322034_1130873733654870_1534996629517703179_n.jpg?oh=b78c424e8c44ac55e031b12455f8d9d3&oe=5A02397D"
            };

            faceDict.Add("Trey", treyList);

            // For each person, upload their faceList to the cloud and write their name and their faceList ID to a file for later reference
            foreach (KeyValuePair<string, string[]> person in faceDict)
            {
                string faceListId = Guid.NewGuid().ToString();
                await faceServiceClient.CreateFaceListAsync(faceListId, person.Key);

                string[] imageUrls = person.Value;
                for (int i = 0; i < imageUrls.Length; i++)
                {
                    await faceServiceClient.AddFaceToFaceListAsync(faceListId, imageUrls[i]);
                }

                string faceListIdTxt = person.Key + "|" + faceListId;
                
                using (StreamWriter sw = File.AppendText(textFilePath))
                {
                    sw.WriteLine(faceListIdTxt);
                }
            }
        }

        private Dictionary<string, string> GetFaceDict()
        {
            // Get peoples' faceLists IDs from the external file that we wrote to
            string line;
            Dictionary<string, string> faceDict = new Dictionary<string, string>();

            StreamReader file = new StreamReader(textFilePath);

            while ((line = file.ReadLine()) != null)
            {
                string[] faceInfo = line.Split('|');
                faceDict.Add(faceInfo[0], faceInfo[1]);
            }

            file.Close();
            return faceDict;
        }

        private async Task<string> GetSimilarFaces(Face face, Dictionary<string, string> faceDict)
        {
            double maxConfidence = 0;
            string maxPerson = "";

            foreach (KeyValuePair<string, string> person in faceDict)
            {
                // Compare each face against a faceList to see if it corresponds to this person
                SimilarPersistedFace[] similarFaceIds = await faceServiceClient.FindSimilarAsync(face.FaceId, person.Value, FindSimilarMatchMode.matchFace);

                // Get average of confidence levels from each face in the faceList to get better face detection accuracy
                double confidenceTotal = 0;
                foreach(SimilarPersistedFace persistedFace in similarFaceIds)
                {
                    confidenceTotal += persistedFace.Confidence;
                }

                double confidenceAvg = confidenceTotal / similarFaceIds.Length;
                if (confidenceAvg > maxConfidence)
                {
                    maxConfidence = confidenceAvg;
                    maxPerson = person.Key;
                }

                Console.Write("Confidence {0} --- Person: {1}", confidenceAvg, person.Key);
            }

            if (maxConfidence >= 0.5)
            {
                return maxPerson;
            }
            else
            {
                return null;
            }
        }
    }
}