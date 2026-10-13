using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using TCG.Client.Cards;
using TCG.Client.UserAssets;
using TCG.Core.Data;
using TCG.Core.State;
using UnityEngine;
using UnityEngine.TestTools;

namespace TCG.Tests.Client
{
    /// <summary>유저 에셋 가져오기: 그림 영역 자르기·저장, 사운드 길이 검사, 카드 화면에 그림 표시 (GameDesign §9).</summary>
    public class UserAssetPlayModeTests
    {
        string _dir;
        LocalUserAssetStore _store;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Application.temporaryCachePath, "UserAssetTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _store = new LocalUserAssetStore(Path.Combine(_dir, "store"));
            UserAssetService.UseStore(_store);
        }

        [TearDown]
        public void TearDown()
        {
            UserAssetService.UseStore(null);
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        [Test]
        public void Image_CropAndSave_AsCardArtSize()
        {
            // 왼쪽 절반 빨강, 오른쪽 절반 파랑인 2000x1000 이미지
            var src = new Texture2D(2000, 1000, TextureFormat.RGBA32, false);
            var pixels = new Color32[2000 * 1000];
            for (int y = 0; y < 1000; y++)
                for (int x = 0; x < 2000; x++)
                    pixels[y * 2000 + x] = x < 1000 ? new Color32(255, 0, 0, 255) : new Color32(0, 0, 255, 255);
            src.SetPixels32(pixels);
            src.Apply();
            string path = Path.Combine(_dir, "big.png");
            File.WriteAllBytes(path, src.EncodeToPNG());
            UnityEngine.Object.Destroy(src);

            var importer = UserAssetService.Importer;
            var limits = UserAssetService.Limits;
            var loaded = importer.LoadSourceImage(path, out string error);
            Assert.That(error, Is.Null);
            Assert.That((loaded.width, loaded.height), Is.EqualTo((2000, 1000)));

            // 왼쪽 끝으로 붙인 영역 → 전부 빨강
            var crop = ArtCrop.Compute(loaded.width, loaded.height, limits.ArtWidth, limits.ArtHeight, 100, 0, 500, limits.MaxArtZoomPercent);
            string hash = importer.SaveArt(loaded, crop);
            UnityEngine.Object.Destroy(loaded);

            Assert.That(AssetHash.IsValid(hash), Is.True);
            Assert.That(_store.PathOf(hash), Does.EndWith(".jpg"));
            var art = UserAssetService.Cache.Texture(hash);
            Assert.That((art.width, art.height), Is.EqualTo((limits.ArtWidth, limits.ArtHeight)));
            // 캐시 텍스처는 읽기 불가(메모리 절약)라 저장 파일을 따로 읽어 색을 본다.
            var readable = new Texture2D(2, 2);
            readable.LoadImage(_store.Load(hash));
            var center = readable.GetPixel(readable.width / 2, readable.height / 2);
            UnityEngine.Object.Destroy(readable);
            Assert.That(center.r, Is.GreaterThan(0.8f));
            Assert.That(center.b, Is.LessThan(0.2f));

            // 같은 내용은 같은 해시
            Assert.That(_store.Put(File.ReadAllBytes(_store.PathOf(hash)), "jpg"), Is.EqualTo(hash));
        }

        [Test]
        public void Image_WrongExtension_Rejected()
        {
            string path = Path.Combine(_dir, "a.gif");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            Assert.That(UserAssetService.Importer.LoadSourceImage(path, out string error), Is.Null);
            Assert.That(error, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator Sound_AcceptsUpToLimit_RejectsLonger()
        {
            string shortPath = Path.Combine(_dir, "short.wav");
            string longPath = Path.Combine(_dir, "long.wav");
            File.WriteAllBytes(shortPath, Wav(1f));
            File.WriteAllBytes(longPath, Wav(6f));

            string hash = null, error = null;
            yield return UserAssetService.Importer.ImportSound(shortPath, (h, e) => { hash = h; error = e; });
            Assert.That(error, Is.Null);
            Assert.That(_store.Contains(hash), Is.True);

            yield return UserAssetService.Cache.LoadClip(hash);
            Assert.That(UserAssetService.Cache.Clip(hash).length, Is.EqualTo(1f).Within(0.05f));

            hash = null;
            error = null;
            yield return UserAssetService.Importer.ImportSound(longPath, (h, e) => { hash = h; error = e; });
            Assert.That(hash, Is.Null);
            Assert.That(error, Does.Contain("5"));
        }

        [Test]
        public void CardDetail_ShowsUserArt()
        {
            var tex = new Texture2D(8, 8);
            string hash = _store.Put(tex.EncodeToJPG(), "jpg");
            UnityEngine.Object.Destroy(tex);

            var canvas = new GameObject("Canvas", typeof(Canvas));
            var view = CardDetailView.Create(canvas.transform);
            var def = new CardDefinition { Id = "c_art", Name = "그림", Type = CardType.Spell, Cost = 1 };
            def.Art.Image = hash;
            view.Show(new Entity(1, 0, def), null);

            var image = view.transform.Find("Art/ArtImage");
            Assert.That(image.gameObject.activeSelf, Is.True);
            UnityEngine.Object.Destroy(canvas);
        }

        /// <summary>16비트 모노 44.1kHz 무음 WAV.</summary>
        static byte[] Wav(float seconds)
        {
            const int rate = 44100;
            int samples = (int)(rate * seconds);
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + samples * 2);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(rate);
                w.Write(rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(samples * 2);
                w.Write(new byte[samples * 2]);
                return ms.ToArray();
            }
        }
    }
}
