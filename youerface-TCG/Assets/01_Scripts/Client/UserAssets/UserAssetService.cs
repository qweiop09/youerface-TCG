using TCG.Core.Data;

namespace TCG.Client.UserAssets
{
    /// <summary>유저 에셋 기능 묶음 (저장소·캐시·가져오기·파일 창). 장면이 바뀌어도 하나를 쓴다.</summary>
    public static class UserAssetService
    {
        static IUserAssetStore _store;
        static UserAssetCache _cache;
        static UserAssetImporter _importer;
        static IFilePicker _picker;

        public static AssetLimits Limits { get; } = AssetLimits.CreateDefault();
        public static IUserAssetStore Store => _store ?? (_store = new LocalUserAssetStore());
        public static UserAssetCache Cache => _cache ?? (_cache = new UserAssetCache(Store));
        public static UserAssetImporter Importer => _importer ?? (_importer = new UserAssetImporter(Store, Limits));
        public static IFilePicker Picker
        {
            get => _picker ?? (_picker = new StandaloneFilePicker());
            set => _picker = value;
        }

        /// <summary>테스트용: 다른 저장소로 바꾼다.</summary>
        public static void UseStore(IUserAssetStore store)
        {
            _store = store;
            _cache = null;
            _importer = null;
        }
    }
}
