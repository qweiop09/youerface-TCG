using System;
using System.Collections.Generic;
using System.IO;
using TCG.Client.CardMaker;
using UnityEngine;

namespace TCG.Client.Common
{
    /// <summary>
    /// 카드 즐겨찾기 (내 컴퓨터의 설정, 카드 데이터와 별개 — 샘플 카드도 즐겨찾기할 수 있고 점수·대전과 무관).
    /// 저장 위치: persistentDataPath/favorites.txt (카드 ID 한 줄에 하나, 즐겨찾기한 순서).
    /// </summary>
    public static class FavoriteCards
    {
        static List<string> s_ids;

        static string FilePath => Path.Combine(Application.persistentDataPath, "favorites.txt");

        static List<string> Ids
        {
            get
            {
                if (s_ids != null)
                    return s_ids;
                s_ids = new List<string>();
                try
                {
                    if (File.Exists(FilePath))
                    {
                        foreach (var line in File.ReadAllLines(FilePath))
                        {
                            string id = line.Trim();
                            if (id.Length > 0 && !s_ids.Contains(id))
                                s_ids.Add(id);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("즐겨찾기를 읽지 못했습니다: " + e.Message);
                }
                return s_ids;
            }
        }

        public static bool Contains(string cardId) => cardId != null && Ids.Contains(cardId);

        /// <summary>켜져 있으면 끄고 꺼져 있으면 켠다. 바뀐 뒤 상태를 돌려준다.</summary>
        public static bool Toggle(string cardId)
        {
            if (string.IsNullOrEmpty(cardId))
                return false;
            bool on = !Ids.Remove(cardId);
            if (on)
                Ids.Add(cardId);
            Save();
            return on;
        }

        static void Save()
        {
            try
            {
                LocalCardLibrary.WriteAtomic(FilePath, string.Join("\n", Ids));
            }
            catch (Exception e)
            {
                Debug.LogWarning("즐겨찾기를 저장하지 못했습니다: " + e.Message);
            }
        }
    }
}
