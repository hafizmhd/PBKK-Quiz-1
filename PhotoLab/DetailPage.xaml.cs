﻿//  ---------------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// 
//  The MIT License (MIT)
// 
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
// 
//  The above copyright notice and this permission notice shall be included in
//  all copies or substantial portions of the Software.
// 
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//  THE SOFTWARE.
//  ---------------------------------------------------------------------------------

using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace PhotoLab
{
    public sealed partial class DetailPage : Page
    {
        ImageFileInfo item;
        CultureInfo culture = CultureInfo.CurrentCulture;
        bool canNavigateWithUnsavedChanges = false;
        BitmapImage ImageSource = null;

        public DetailPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            item = e.Parameter as ImageFileInfo;
            canNavigateWithUnsavedChanges = false;
            ImageSource = await item.GetImageSourceAsync();

            if (item != null)
            {
                ConnectedAnimation imageAnimation = ConnectedAnimationService.GetForCurrentView().GetAnimation("itemAnimation");
                if (imageAnimation != null)
                {
                    targetImage.Source = ImageSource;

                    imageAnimation.Completed += async (s, e_) =>
                    {
                        await LoadBrushAsync();
                        // This delay prevents a flicker that can happen when
                        // a large image is being loaded to the brush. It gives
                        // the image time to finish loading. (200 is ok, 400 to be safe.)
                        await Task.Delay(400);
                        targetImage.Source = null;
                    };
                    imageAnimation.TryStart(targetImage);
                }

                if (ImageSource.PixelHeight == 0 && ImageSource.PixelWidth == 0)
                {
                    // There is no editable image loaded. Disable zoom and edit
                    // to prevent other errors.
                    EditButton.IsEnabled = false;
                    ZoomButton.IsEnabled = false;
                };
            }
            else
            {
                // error
            }

            if (this.Frame.CanGoBack)
            {
                SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Visible;
            }
            else
            {
                SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed;
            }

            base.OnNavigatedTo(e);
        }

        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            // If the photo has unsaved changes, we want to show a dialog
            // that warns the user before the navigation happens
            // To give the user a chance to view the dialog and respond,
            // we go ahead and cancel the navigation.
            // If the user wants to leave the page, we restart the
            // navigation. We use the canNavigateWithUnsavedChanges field to
            // track whether the user has been asked.
            if (item.NeedsSaved && !canNavigateWithUnsavedChanges)
            {
                // The item has unsaved changes and we haven't shown the
                // dialog yet. Cancel navigation and show the dialog.
                e.Cancel = true;
                ShowSaveDialog(e);
            }
            else
            {
                if (e.NavigationMode == NavigationMode.Back)
                {
                    ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("backAnimation", imgRect);
                }
                canNavigateWithUnsavedChanges = false;
                base.OnNavigatingFrom(e);
            }
        }

        /// <summary>
        /// Gives users a chance to save the image before navigating
        /// to a different page.
        /// </summary>
        private async void ShowSaveDialog(NavigatingCancelEventArgs e)
        {
            ContentDialog saveDialog = new ContentDialog()
            {
                Title = "Unsaved changes",
                Content = "You have unsaved changes that will be lost if you leave this page.",
                PrimaryButtonText = "Leave this page",
                SecondaryButtonText = "Stay"
            };
            ContentDialogResult result = await saveDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                // The user decided to leave the page. Restart
                // the navigation attempt. 
                canNavigateWithUnsavedChanges = true;
                ResetEffects();
                Frame.Navigate(e.SourcePageType, e.Parameter);
            }
        }

        private void ZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (MainImageScroller != null)
            {
                MainImageScroller.ChangeView(null, null, (float)e.NewValue);
            }
        }

        private void MainImageScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            ZoomSlider.Value = ((ScrollViewer)sender).ZoomFactor;
        }

        private void FitToScreen()
        {
            var zoomFactor = (float)Math.Min(MainImageScroller.ActualWidth / item.ImageProperties.Width,
                MainImageScroller.ActualHeight / item.ImageProperties.Height);
            MainImageScroller.ChangeView(null, null, zoomFactor);
        }

        private void ShowActualSize()
        {
            MainImageScroller.ChangeView(null, null, 1);
        }

        private void UpdateZoomState()
        {
            if (MainImageScroller.ZoomFactor == 1)
            {
                FitToScreen();
            }
            else
            {
                ShowActualSize();
            }
        }

        private void ToggleEditState()
        {
            MainSplitView.IsPaneOpen = !MainSplitView.IsPaneOpen;
        }

        private async void ExportImage()
        {
            CanvasDevice device = CanvasDevice.GetSharedDevice();
            using (CanvasRenderTarget offscreen = new CanvasRenderTarget(
                device, item.ImageProperties.Width, item.ImageProperties.Height, 96))
            {
                using (IRandomAccessStream stream = await item.ImageFile.OpenReadAsync())
                using (CanvasBitmap image = await CanvasBitmap.LoadAsync(offscreen, stream, 96))
                {
                    ImageEffectsBrush.SetSource(image);

                    using (CanvasDrawingSession ds = offscreen.CreateDrawingSession())
                    {
                        ds.Clear(Windows.UI.Colors.Black);

                        var img = ImageEffectsBrush.Image;
                        ds.DrawImage(img);
                    }

                    var fileSavePicker = new FileSavePicker()
                    {
                        SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                        SuggestedSaveFile = item.ImageFile
                    };

                    fileSavePicker.FileTypeChoices.Add("JPEG files", new List<string>() { ".jpg" });

                    var outputFile = await fileSavePicker.PickSaveFileAsync();

                    if (outputFile != null)
                    {
                        using (IRandomAccessStream outStream = await outputFile.OpenAsync(FileAccessMode.ReadWrite))
                        {
                            await offscreen.SaveAsync(outStream, CanvasBitmapFileFormat.Jpeg);
                        }

                        // Check whether this save is overwriting the original image.
                        // If it is, replace it in the list. Otherwise, insert it as a copy.
                        bool replace = false;
                        if (outputFile.IsEqual(item.ImageFile))
                        {
                            replace = true;
                        }

                        try
                        {
                            await LoadSavedImageAsync(outputFile, replace);
                        }
                        catch (Exception ex)
                        {
                            if (ex.Message.Contains("0x80070323"))
                            {
                                // The handle with which this oplock was associated has been closed.
                                // The oplock is now broken. (Exception from HRESULT: 0x80070323)
                                // This is a temporary condition, so just try again.
                                await LoadSavedImageAsync(outputFile, replace);
                            }
                        }
                    }
                }
            }
        }

        private async Task LoadSavedImageAsync(StorageFile imageFile, bool replaceImage)
        {
            item.NeedsSaved = false;
            var newItem = await MainPage.LoadImageInfo(imageFile);
            ResetEffects();

            // Get the index of the original image so we can
            // insert the saved image in the same place.
            var index = MainPage.Current.Images.IndexOf(item);

            item = newItem;
            this.Bindings.Update();

            UnloadObject(imgRect);
            FindName("imgRect");
            await LoadBrushAsync();

            // Insert the saved image into the collection.
            if (replaceImage == true)
            {
                MainPage.Current.Images.RemoveAt(index);
                MainPage.Current.Images.Insert(index, item);
            }
            else
            {
                MainPage.Current.Images.Insert(index+1, item);
            }
            

            // Replace the persisted image used for connected animation.
            MainPage.Current.UpdatePersistedItem(item);

            // Update BitMapImage used for small picture.
            ImageSource = await item.GetImageSourceAsync();
            SmallImage.Source = ImageSource;
        }

        private async Task LoadBrushAsync()
        {
            using (IRandomAccessStream fileStream = await item.ImageFile.OpenReadAsync())
            {
                ImageEffectsBrush.LoadImageFromStream(fileStream);
            }
        }

        private void ResetEffects()
        {
            item.Exposure =
                item.Blur =
                item.Tint =
                item.Temperature =
                item.Contrast = 0;
            item.Saturation = 1;
        }

        private void SmallImage_Loaded(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            SmallImage.Source = ImageSource;
        }
    }
}
