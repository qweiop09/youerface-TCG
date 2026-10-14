using System.Collections.Generic;
using TCG.Core.Commands;
using TCG.Core.Data;
using TCG.Core.Net;

namespace TCG.Client.Online
{
    /// <summary>
    /// 대전 화면(<see cref="Battle.BattleSession"/>)이 보는 온라인 상대. 방장이면 <see cref="OnlineHost"/>, 손님이면 <see cref="OnlineGuest"/>를 감싼다.
    /// 화면은 시야 상태 변화(<see cref="SeatUpdate"/>)만 받고 명령만 낸다.
    /// </summary>
    public interface IOnlineLink
    {
        int Seat { get; }
        bool TryDequeue(out SeatUpdate update);
        /// <summary>명령을 낸다. 방장은 바로 검사 결과, 손님은 보냈으면 true (거절은 나중에 <see cref="TakeRejection"/>).</summary>
        bool Submit(Command command, out string reason);
        string TakeRejection();
        MatchClock Clock { get; }
        /// <summary>연결 문제 안내 (없으면 null).</summary>
        string ConnectionNote { get; }
        bool Ended { get; }
        MatchEndReason EndReason { get; }
        string OpponentName { get; }
        /// <summary>대전에 나오는 카드 (사운드 미리 읽기용).</summary>
        IReadOnlyList<CardDefinition> MatchCards { get; }
    }

    public sealed class HostLink : IOnlineLink
    {
        readonly OnlineHost _host;
        readonly List<CardDefinition> _cards = new List<CardDefinition>();

        public HostLink(OnlineHost host, IReadOnlyList<CardDefinition> hostCards)
        {
            _host = host;
            _cards.AddRange(hostCards);
            if (host.GuestCards != null)
                _cards.AddRange(host.GuestCards);
        }

        public int Seat => OnlineHost.HostSeat;
        public bool TryDequeue(out SeatUpdate update)
        {
            if (_host.LocalUpdates.Count > 0)
            {
                update = _host.LocalUpdates.Dequeue();
                return true;
            }
            update = null;
            return false;
        }

        public bool Submit(Command command, out string reason)
        {
            var result = _host.Submit(command);
            reason = result.Reason;
            return result.IsValid;
        }

        public string TakeRejection() => null;
        public MatchClock Clock => _host.Match?.Clock();
        public bool Ended => _host.Match != null && !_host.Match.InProgress;
        public MatchEndReason EndReason => _host.Match?.EndReason ?? MatchEndReason.None;
        public string OpponentName => _host.GuestName;
        public IReadOnlyList<CardDefinition> MatchCards => _cards;

        public string ConnectionNote
        {
            get
            {
                var match = _host.Match;
                if (match == null || !match.InProgress || match.Connected[OnlineHost.GuestSeat])
                    return null;
                var clock = match.Clock();
                return clock.Mover == OnlineHost.GuestSeat
                    ? $"상대 연결 끊김 — {Seconds(clock.DisconnectRemainingMs)}초 뒤 자동 진행 ({match.DisconnectAutos[OnlineHost.GuestSeat]}/{match.Rules.MaxDisconnectedAutoActions})"
                    : "상대 연결 끊김 — 재접속을 기다리는 중";
            }
        }

        internal static int Seconds(int ms) => (ms + 999) / 1000;
    }

    public sealed class GuestLink : IOnlineLink
    {
        readonly OnlineGuest _guest;
        readonly List<CardDefinition> _cards = new List<CardDefinition>();

        public GuestLink(OnlineGuest guest, IReadOnlyList<CardDefinition> myCards)
        {
            _guest = guest;
            _cards.AddRange(myCards);
            if (guest.HostCards != null)
                _cards.AddRange(guest.HostCards);
        }

        public int Seat => _guest.Seat;
        public bool TryDequeue(out SeatUpdate update)
        {
            if (_guest.Updates.Count > 0)
            {
                update = _guest.Updates.Dequeue();
                return true;
            }
            update = null;
            return false;
        }

        public bool Submit(Command command, out string reason)
        {
            reason = null;
            if (_guest.Submit(command) > 0)
                return true;
            reason = "방장과 연결되어 있지 않습니다.";
            return false;
        }

        public string TakeRejection()
        {
            string reason = _guest.LastCommandRejection;
            _guest.LastCommandRejection = null;
            return reason;
        }

        public MatchClock Clock => _guest.Clock;
        public bool Ended => _guest.Phase == GuestPhase.Ended;
        public MatchEndReason EndReason => _guest.EndReason;
        public string OpponentName => _guest.HostName;
        public IReadOnlyList<CardDefinition> MatchCards => _cards;

        public string ConnectionNote
        {
            get
            {
                if (_guest.Phase == GuestPhase.Reconnecting)
                    return $"방장 연결 끊김 — 재접속 대기 {HostLink.Seconds(_guest.ReconnectRemainingMs)}초";
                if (_guest.Phase == GuestPhase.Ended && _guest.EndReason == MatchEndReason.HostLost)
                    return "방장이 돌아오지 않아 대전을 끝냈습니다";
                return null;
            }
        }
    }
}
