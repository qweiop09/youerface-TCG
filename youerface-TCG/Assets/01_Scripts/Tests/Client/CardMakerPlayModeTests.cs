using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using TCG.Client.CardMaker;
using TCG.Client.UserAssets;
using TCG.Core.Data;
using TCG.Core.Scoring;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TCG.Tests.Client
{
    /// <summary>카드 제작 화면 (M4-3·M4-4): 편집 → 점수·검사 갱신, 소환물 들어가기, 저장·복제·삭제·열기, 그림 영역 창.</summary>
    public class CardMakerPlayModeTests
    {
        string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Application.temporaryCachePath, "CardMakerTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        /// <summary>row가 있으면 그 줄("Row 이름") 안의 버튼만 찾는다.</summary>
        static Button FindButton(CardMakerScreen screen, string name, string row = null)
        {
            foreach (var b in screen.GetComponentsInChildren<Button>(false))
            {
                if (b.name == name && (row == null || b.transform.parent.name == "Row " + row))
                    return b;
            }
            return null;
        }

        /// <summary>
        /// Overlay UI까지 찍는다. batchmode에선 WaitForEndOfFrame이 오지 않아, 잠깐 Overlay 캔버스를 임시 카메라용으로 바꿔 렌더한다.
        /// </summary>
        internal static IEnumerator CaptureScreen(string path)
        {
            yield return null;
            var cameraGo = new GameObject("CaptureCamera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>();
            var switched = new System.Collections.Generic.List<Canvas>();
            foreach (var c in canvases)
            {
                if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = camera;
                    c.planeDistance = 1f + (1000 - c.sortingOrder) * 0.001f;
                    switched.Add(c);
                }
            }
            var rt = new RenderTexture(1920, 1080, 24);
            camera.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = rt;
            var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
            foreach (var c in switched)
            {
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                c.worldCamera = null;
            }
            camera.targetTexture = null;
            rt.Release();
            UnityEngine.Object.Destroy(cameraGo);
        }

        [UnityTest]
        public IEnumerator Edit_Score_Summon_SaveLoad()
        {
            SceneManager.LoadScene(CardMakerScreen.SceneName);
            yield return null;
            yield return null;
            var screen = UnityEngine.Object.FindAnyObjectByType<CardMakerScreen>();
            screen.Library = new LocalCardLibrary(Path.Combine(_dir, "cards"));
            screen.Decks = new TCG.Client.DeckBuilder.LocalDeckLibrary(Path.Combine(_dir, "decks"));

            // 새 카드: 1코스트 1/1 기물 → -10 + 20 - 20
            Assert.That(screen.Card.Type, Is.EqualTo(CardType.Minion));
            Assert.That(screen.CurrentScore, Is.EqualTo(-10));
            Assert.That(screen.Problems, Is.Empty);

            // 공격력 + 버튼 → +10
            FindButton(screen, "Plus", "공격력").onClick.Invoke();
            yield return null;
            Assert.That(screen.Card.Attack, Is.EqualTo(2));
            Assert.That(screen.CurrentScore, Is.EqualTo(0));

            // 효과 추가 (등장 시 상대 영웅 1 피해 +10)
            FindButton(screen, "Button + 효과 추가").onClick.Invoke();
            yield return null;
            Assert.That(screen.Card.Effects.Count, Is.EqualTo(1));
            Assert.That(screen.CurrentScore, Is.EqualTo(ScoreCalculator.Calculate(screen.Card, ScoreTable.CreateDefault())));
            Assert.That(screen.CurrentScore, Is.EqualTo(10));

            string dir = Environment.GetEnvironmentVariable("TCG_CAPTURE_DIR");
            if (dir != null)
            {
                yield return null;
                yield return CaptureScreen(Path.Combine(dir, "cardmaker.png"));
            }

            // 소환물 생성으로 바꾸고 소환물로 들어가기
            var card = CardEditing.Clone(screen.Card);
            CardEditing.SetAction(card.Effects[0], ActionType.CreateSummon);
            screen.Open(card);
            screen.EnterSummon(screen.Card.Effects[0]);
            yield return null;
            Assert.That(screen.Editing.Type, Is.EqualTo(CardType.Summon));
            if (dir != null)
            {
                yield return null;
                yield return CaptureScreen(Path.Combine(dir, "cardmaker_summon.png"));
            }
            screen.LeaveTo(0);
            Assert.That(screen.Editing, Is.SameAs(screen.Card));

            // 저장 → 복제·저장 → 삭제(두 번) → 열기
            // 연 직후는 저장된 것과 같다고 본다. 바꾸면 "저장 안 됨"
            Assert.That(screen.IsDirty, Is.False);
            screen.Card.Name = "불씨 술사";
            Assert.That(screen.IsDirty, Is.True);
            screen.Save();
            Assert.That(screen.IsDirty, Is.False);
            string firstId = screen.Card.Id;
            screen.Duplicate();
            Assert.That(screen.Card.Id, Is.Not.EqualTo(firstId));
            screen.Save();
            Assert.That(screen.Library.List().Count, Is.EqualTo(2));

            screen.OpenLibrary();
            yield return null;
            Assert.That(screen.LibraryOpen, Is.True);
            if (dir != null)
            {
                yield return null;
                yield return CaptureScreen(Path.Combine(dir, "cardmaker_library.png"));
            }
            string copyId = screen.Card.Id;
            screen.Decks.Save(new DeckDefinition { Id = "d_user", Name = "쓰는 덱", Cards = { copyId } });
            screen.DeleteFromLibrary(copyId);
            Assert.That(screen.Library.Exists(copyId), Is.True, "첫 번째는 확인만");
            Assert.That(screen.StatusText, Does.Contain("쓰는 덱"), "이 카드를 쓰는 덱 안내");
            screen.DeleteFromLibrary(copyId);
            Assert.That(screen.Library.Exists(copyId), Is.False);

            screen.OpenFromLibrary(firstId);
            Assert.That(screen.Card.Id, Is.EqualTo(firstId));
            Assert.That(screen.Card.Effects[0].Action.Type, Is.EqualTo(ActionType.CreateSummon));
            Assert.That(screen.LibraryOpen, Is.False);
            Assert.That(screen.IsDirty, Is.False);
        }

        [UnityTest]
        public IEnumerator ArtCropDialog_ZoomAndCenter_ReturnsSameCropAsCore()
        {
            var host = new GameObject("Host");
            var source = new Texture2D(1200, 800);
            var limits = UserAssetService.Limits;
            CropRect? result = null;
            bool closed = false;
            var dialog = ArtCropDialog.Open(host.transform, source, limits, r => { result = r; closed = true; });
            yield return null;

            dialog.SetZoom(200);
            dialog.SetCenter(100, 700);
            var expected = ArtCrop.Compute(1200, 800, limits.ArtWidth, limits.ArtHeight, 200, 100, 700, limits.MaxArtZoomPercent);
            Assert.That((dialog.Crop.X, dialog.Crop.Y, dialog.Crop.Width, dialog.Crop.Height),
                Is.EqualTo((expected.X, expected.Y, expected.Width, expected.Height)));

            string dir = Environment.GetEnvironmentVariable("TCG_CAPTURE_DIR");
            if (dir != null)
                yield return CaptureScreen(Path.Combine(dir, "art_crop.png"));

            foreach (var b in dialog.GetComponentsInChildren<Button>())
            {
                if (b.name == "Confirm")
                    b.onClick.Invoke();
            }
            Assert.That(closed, Is.True);
            Assert.That(result.HasValue, Is.True);
            Assert.That(result.Value.Width, Is.EqualTo(expected.Width));

            UnityEngine.Object.Destroy(source);
            UnityEngine.Object.Destroy(host);
        }
    }
}
