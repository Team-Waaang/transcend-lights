using System;
using System.Collections.Generic;
using UnityEngine;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace PhonePointer
{
    public static class QrUtility
    {
        // Keep the changing QR inside the fixed AR marker. Its built-in white quiet zone is retained.
        public const float RegistrationQrFraction=.48f;
        public static Rect CombinedQrRect(Rect marker)
        {
            var size=marker.size*RegistrationQrFraction;
            return new Rect(marker.center-size*.5f,size);
        }
        public static Texture2D Encode(string value)
        {
            var matrix = new QRCodeWriter().encode(value, BarcodeFormat.QR_CODE, 512, 512,
                new Dictionary<EncodeHintType, object> { { EncodeHintType.MARGIN, 4 }, { EncodeHintType.ERROR_CORRECTION, ZXing.QrCode.Internal.ErrorCorrectionLevel.M } });
            var texture = new Texture2D(matrix.Width, matrix.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var pixels = new Color32[matrix.Width * matrix.Height];
            for (int y = 0; y < matrix.Height; y++) for (int x = 0; x < matrix.Width; x++)
                pixels[(matrix.Height - 1 - y) * matrix.Width + x] = matrix[x, y] ? new Color32(0,0,0,255) : new Color32(255,255,255,255);
            texture.SetPixels32(pixels); texture.Apply(); return texture;
        }
        public static string Decode(byte[] grayscale, int width, int height)
        {
            var reader = new BarcodeReaderGeneric { AutoRotate = true, Options = new DecodingOptions { TryHarder = true, PossibleFormats = new[] { BarcodeFormat.QR_CODE } } };
            return reader.Decode(grayscale, width, height, RGBLuminanceSource.BitmapFormat.Gray8)?.Text;
        }
    }
}
