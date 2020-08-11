﻿namespace SimpleDICOMToolkit.ViewModels
{
    using Dicom;
    using Dicom.Imaging;
    using Stylet;
    using StyletIoC;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Media.Imaging;
    using Client;
    using Helpers;
    using Models;
    using Services;

    public class PrintPreviewViewModel : Screen, IHandle<ClientMessageItem>, IDisposable
    {
        private readonly IEventAggregator _eventAggregator;

        [Inject]
        private II18nService _i18NService;

        [Inject]
        private INotificationService _notificationService;

        [Inject]
        private IPrintSCU _printSCU;

        private List<BitmapSource> _images = new List<BitmapSource>();

        private int _currentIndex = 0;

        public int CurrentIndex
        {
            get => _currentIndex;
            private set
            {
                SetAndNotify(ref _currentIndex, value);
                NotifyOfPropertyChange(() => ImageSource);
                NotifyOfPropertyChange(() => CanShowPrev);
                NotifyOfPropertyChange(() => CanShowNext);
            }
        }

        public BitmapSource ImageSource
        {
            get
            {
                if (_currentIndex < _images.Count)
                {
                    return _images[_currentIndex];
                }

                return null;
            }
        }

        public PrintPreviewViewModel(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            _eventAggregator.Subscribe(this, nameof(PrintPreviewViewModel));
        }

        public async void Handle(ClientMessageItem message)
        {
            if (ImageSource == null)
                return;

            _eventAggregator.Publish(new BusyStateItem(true), nameof(PrintPreviewViewModel));

            PrintOptions options = GetPrintOptions();

            List<Bitmap> images = new List<Bitmap>();
            foreach (var image in _images)
            {
#if NET_CORE
                images.Add(((BitmapImage)image).AsBitmap());
#else
                images.Add(((WriteableBitmap)image).AsBitmap());
#endif
            }

            try
            {
                await _printSCU.PrintImagesAsync(message.ServerIP, message.ServerPort, message.ServerAET, message.LocalAET, options, images);

                string content = string.Format(
                    _i18NService.GetXmlStringByKey("ToastPrintResult"),
                    _i18NService.GetXmlStringByKey("Success"));
                // 这里不使用 await，否则当前线程会阻塞直到toast显示完成
                _ = _notificationService.ShowToastAsync(content, new TimeSpan(0, 0, 3));
            }
            finally
            {
                foreach (var image in images)
                {
                    image.Dispose();
                }

                _eventAggregator.Publish(new BusyStateItem(false), nameof(PrintPreviewViewModel));
            }
        }

        public void OnDragFilesOver(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Link;
            else
                e.Effects = DragDropEffects.None;

            e.Handled = true;
        }

        public async void OnDropFiles(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            Array files = e.Data.GetData(DataFormats.FileDrop) as Array;

            foreach (object file in files)
            {
                if (!File.Exists(file.ToString()))
                {
                    continue;
                }

                await AddDcmImage(file.ToString());
            }
        }

        public async Task AddSampleImage()
        {
            await AddDcmImage(Environment.CurrentDirectory + "\\Fate.dcm");
        }

        private PrintOptions GetPrintOptions()
        {
            return (Parent as PrintViewModel).PrintOptions;
        }

        private async Task AddDcmImage(string file)
        {
            if (!File.Exists(file))
                return;

            DicomFile dicomFile = await DicomFile.OpenAsync(file);

            DicomImage image = new DicomImage(dicomFile.Dataset);

            using (IImage iimage = image.RenderImage())
            {
#if NET_CORE
                _images.Add(iimage.AsSharedBitmap().AsBitmapImage());
#else
                _images.Add(iimage.AsWriteableBitmap());
#endif
            }

            // update Display
            CurrentIndex = _images.Count - 1;
        }

        public void RemoveCurrentImage()
        {
            _images.RemoveAt(_currentIndex);

            if (!_images.Any())
            {
                CurrentIndex = 0;
                return;
            }

            if (_currentIndex < _images.Count)
            {
                CurrentIndex = _currentIndex;
            }
            else
            {
                CurrentIndex -= 1;
            }
        }

        public bool CanShowPrev => CurrentIndex > 0;
        public bool CanShowNext => CurrentIndex < _images.Count - 1;

        public void ShowPrev()
        {
            if (_currentIndex > 0)
            {
                CurrentIndex -= 1;
            }
        }

        public void ShowNext()
        {
            if (_currentIndex < _images.Count - 1)
            {
                CurrentIndex += 1;
            }
        }

        public void Dispose()
        {
            _eventAggregator.Unsubscribe(this);
        }
    }
}
