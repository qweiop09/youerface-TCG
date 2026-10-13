namespace TCG.Client.UserAssets
{
    /// <summary>
    /// 유저 에셋 저장소. 파일 내용의 SHA-256 해시("sha256:...")로 저장·조회한다 (Architecture §7).
    /// 나중에 서버 업로드를 붙여도 이 해시 참조를 그대로 쓴다.
    /// </summary>
    public interface IUserAssetStore
    {
        /// <summary>저장하고 해시를 돌려준다. 같은 내용이 이미 있으면 다시 쓰지 않는다. extension은 점 없이("jpg").</summary>
        string Put(byte[] data, string extension);
        bool Contains(string hash);
        /// <summary>저장된 파일 경로. 없으면 null.</summary>
        string PathOf(string hash);
        byte[] Load(string hash);
    }
}
