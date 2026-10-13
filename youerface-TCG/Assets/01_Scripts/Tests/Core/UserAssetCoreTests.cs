using System.Text;
using NUnit.Framework;
using TCG.Core.Data;

namespace TCG.Tests.Core
{
    /// <summary>유저 에셋 Core 부분: 해시, 그림 영역 계산, 참조 수집 (GameDesign §9).</summary>
    public class UserAssetCoreTests
    {
        [Test]
        public void Hash_IsSha256Hex_AndStable()
        {
            string hash = AssetHash.Compute(Encoding.UTF8.GetBytes("abc"));
            Assert.That(hash, Is.EqualTo("sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
            Assert.That(AssetHash.IsValid(hash), Is.True);
            Assert.That(AssetHash.IsValid("sha256:ab12"), Is.False);
            Assert.That(AssetHash.IsValid("md5:" + new string('a', 64)), Is.False);
        }

        [Test]
        public void Crop_FullZoom_FitsLargestAspectRect_Centered()
        {
            // 1000x500 가로 이미지, 6:5 → 높이 500에 맞춰 너비 600
            var r = ArtCrop.Compute(1000, 500, 6, 5, 100, 500, 250, 400);
            Assert.That((r.X, r.Y, r.Width, r.Height), Is.EqualTo((200, 0, 600, 500)));

            // 세로 이미지 600x1200 → 너비 600에 맞춰 높이 500
            r = ArtCrop.Compute(600, 1200, 6, 5, 100, 300, 600, 400);
            Assert.That((r.X, r.Y, r.Width, r.Height), Is.EqualTo((0, 350, 600, 500)));
        }

        [Test]
        public void Crop_Zoom_ShrinksRect_AndClampsInsideSource()
        {
            var r = ArtCrop.Compute(1000, 500, 6, 5, 200, 0, 0, 400);
            Assert.That((r.X, r.Y, r.Width, r.Height), Is.EqualTo((0, 0, 300, 250)));

            r = ArtCrop.Compute(1000, 500, 6, 5, 200, 10000, 10000, 400);
            Assert.That((r.X, r.Y), Is.EqualTo((700, 250)));

            // 최대 확대를 넘으면 최대로
            r = ArtCrop.Compute(1000, 500, 6, 5, 1000, 500, 250, 400);
            Assert.That(r.Width, Is.EqualTo(150));
        }

        [Test]
        public void AssetRefs_IncludeNestedSummon_NoDuplicates()
        {
            var ember = TestCards.Ember();
            ember.Sounds.Enter = "sha256:ab12";
            var card = TestCards.SummonSpell(ember);
            card.Art.Image = "sha256:ab12";
            card.Sounds.Exit = "sha256:ef56";

            Assert.That(CardAssetRefs.Collect(card), Is.EqualTo(new[] { "sha256:ab12", "sha256:ef56", "sha256:9999" }));
        }
    }
}
