using System.Collections.Generic;
using TCG.Client.UserAssets;
using TCG.Core.AI;
using TCG.Core.Data;
using TCG.Core.Net;
using TCG.Core.Rules;
using TCG.Core.State;
using UnityEngine;

namespace TCG.Client.Online
{
    /// <summary>
    /// 온라인 연결을 장면 사이에서 유지하고 매 프레임 진행한다(DontDestroyOnLoad). 방 화면이 만들고, 대전 화면이 <see cref="Link"/>를 쓴다.
    /// 지금 연결은 메모리 안 가짜 연결(<see cref="LoopbackListener"/>)뿐: 같은 프로그램 안의 컴퓨터 상대가 반대쪽(방장 또는 손님)을 맡는다.
    /// UGS Relay(M6-4)가 붙으면 같은 흐름에 진짜 전송만 바꿔 끼운다.
    /// </summary>
    public sealed class OnlineRunner : MonoBehaviour
    {
        /// <summary>컴퓨터 상대가 행동 전에 쉬는 시간(초).</summary>
        const float AiDelay = 0.9f;
        /// <summary>손님이 끊겼을 때 재접속 시도 간격(초).</summary>
        const float RejoinInterval = 2f;

        LoopbackListener _listener;
        IAiPlayer _ai;
        OnlineHost _aiHost;
        OnlineGuest _aiGuest;
        GameState _aiHostView;
        float _aiWait;
        float _rejoinWait;
        IReadOnlyList<CardDefinition> _myCards;

        public static OnlineRunner Current { get; private set; }

        public OnlineHost Host { get; private set; }
        public OnlineGuest Guest { get; private set; }
        public IOnlineLink Link { get; private set; }
        /// <summary>시험 연결의 손님 쪽 끝 (끊김 시험용).</summary>
        public LoopbackTransport GuestTransport { get; private set; }
        public bool IsHost => Host != null;

        /// <summary>대전이 시작됐다 (화면을 대전으로 넘길 때).</summary>
        public bool InMatch => Link != null;

        public string Status
        {
            get
            {
                if (Host != null)
                    return Host.Status;
                if (Guest == null)
                    return "";
                switch (Guest.Phase)
                {
                    case GuestPhase.Connecting: return "방에 접속하는 중";
                    case GuestPhase.DeckSent: return "방장이 덱을 확인하는 중";
                    case GuestPhase.DeckRejected: return "덱이 방 규칙에 맞지 않습니다: " + string.Join(" / ", Guest.DeckIssues);
                    case GuestPhase.Assets: return "카드 그림·사운드 받는 중";
                    case GuestPhase.Playing: return "대전 시작";
                    case GuestPhase.Rejected: return "접속 거절: " + Guest.RejectReason;
                    default: return Guest.Phase.ToString();
                }
            }
        }

        static OnlineRunner Create(string name)
        {
            Current?.Close();
            var go = new GameObject(name);
            DontDestroyOnLoad(go);
            return Current = go.AddComponent<OnlineRunner>();
        }

        /// <summary>시험: 내가 방장, 컴퓨터가 손님으로 가짜 연결에 들어온다.</summary>
        public static OnlineRunner StartLoopbackAsHost(HostSetup me, GuestSetup computer)
        {
            var runner = Create("Online (loopback host)");
            var assets = new UserAssetSource(UserAssetService.Store);
            runner._listener = new LoopbackListener();
            runner.Host = new OnlineHost(me, runner._listener, assets);
            runner._myCards = me.Cards.Resolve(me.Deck);
            runner.GuestTransport = runner._listener.Connect();
            runner._aiGuest = new OnlineGuest(computer, runner.GuestTransport, assets);
            runner._ai = AiPlayers.Create(AiDifficulty.Normal, me.Seed + 1, new GameEngine());
            return runner;
        }

        /// <summary>시험: 컴퓨터가 방장, 내가 손님으로 들어간다.</summary>
        public static OnlineRunner StartLoopbackAsGuest(HostSetup computer, GuestSetup me)
        {
            var runner = Create("Online (loopback guest)");
            var assets = new UserAssetSource(UserAssetService.Store);
            runner._listener = new LoopbackListener();
            runner._aiHost = new OnlineHost(computer, runner._listener, assets);
            runner._myCards = me.Cards.Resolve(me.Deck);
            runner.GuestTransport = runner._listener.Connect();
            runner.Guest = new OnlineGuest(me, runner.GuestTransport, assets);
            runner._ai = AiPlayers.Create(AiDifficulty.Normal, computer.Seed + 1, new GameEngine());
            return runner;
        }

        void Update()
        {
            int ms = Mathf.RoundToInt(Time.unscaledDeltaTime * 1000f);
            Host?.Update(ms);
            _aiHost?.Update(ms);
            Guest?.Update(ms);
            _aiGuest?.Update(ms);

            if (Link == null)
            {
                if (Host != null && Host.Phase == HostPhase.Playing)
                    Link = new HostLink(Host, _myCards);
                else if (Guest != null && Guest.Phase == GuestPhase.Playing)
                    Link = new GuestLink(Guest, _myCards);
            }

            TryRejoin();
            DriveComputer(Time.unscaledDeltaTime);
        }

        /// <summary>손님이 방장과 끊겼으면 잠깐마다 다시 접속해 본다.</summary>
        void TryRejoin()
        {
            if (Guest == null || Guest.Phase != GuestPhase.Reconnecting || _listener == null)
                return;
            _rejoinWait += Time.unscaledDeltaTime;
            if (_rejoinWait < RejoinInterval)
                return;
            _rejoinWait = 0f;
            GuestTransport = _listener.Connect();
            Guest.Rejoin(GuestTransport);
        }

        /// <summary>시험 연결의 컴퓨터 상대: 자기 시야로 수를 고르고 조금 쉬었다가 낸다.</summary>
        void DriveComputer(float deltaTime)
        {
            GameState view = null;
            int seat = -1;
            if (_aiGuest != null && _aiGuest.Phase == GuestPhase.Playing)
            {
                while (_aiGuest.Updates.Count > 0)
                    _aiGuest.Updates.Dequeue();
                _aiGuest.LastCommandRejection = null;
                view = _aiGuest.View;
                seat = _aiGuest.Seat;
            }
            else if (_aiHost != null && _aiHost.Phase == HostPhase.Playing)
            {
                while (_aiHost.LocalUpdates.Count > 0)
                    _aiHostView = _aiHost.LocalUpdates.Dequeue().After;
                view = _aiHostView;
                seat = OnlineHost.HostSeat;
            }
            if (view == null || view.Phase != GamePhase.InProgress || (view.Decision?.PlayerIndex ?? view.ActivePlayerIndex) != seat)
            {
                _aiWait = 0f;
                return;
            }
            _aiWait += deltaTime;
            if (_aiWait < AiDelay)
                return;
            _aiWait = 0f;
            var command = _ai.ChooseCommand(view, seat);
            if (_aiGuest != null)
                _aiGuest.Submit(command);
            else
                _aiHost.Submit(command);
        }

        /// <summary>시험: 손님 쪽 연결을 끊는다 (재접속은 자동으로 시도).</summary>
        public void DropGuestLink() => GuestTransport?.Drop();

        public void Close()
        {
            Host?.Close();
            _aiHost?.Close();
            Guest?.Close();
            _aiGuest?.Close();
            Host = null;
            Guest = null;
            _aiHost = null;
            _aiGuest = null;
            Link = null;
            if (Current == this)
                Current = null;
            if (this != null)
                Destroy(gameObject);
        }
    }
}
