using System.Collections.Generic;

namespace TCG.Core.Net
{
    public enum LinkState
    {
        Connecting,
        Connected,
        Closed,
    }

    /// <summary>
    /// 상대 1명과의 연결 (Architecture §6). 메시지(문자열) 단위, 순서 보장·유실 없음을 전제한다.
    /// 받기는 폴링(<see cref="TryReceive"/>)이라 Unity 메인 스레드·테스트에서 같은 방식으로 쓴다.
    /// 구현: <see cref="LoopbackTransport"/>(메모리 안 가짜 연결), 나중에 Unity Relay(Client).
    /// </summary>
    public interface ITransport
    {
        LinkState State { get; }
        /// <summary>끊겼으면 버린다.</summary>
        void Send(string message);
        bool TryReceive(out string message);
        void Close();
    }

    /// <summary>호스트가 게스트 연결을 받는 곳 (방).</summary>
    public interface ITransportListener
    {
        bool TryAccept(out ITransport transport);
        void Close();
    }

    /// <summary>
    /// 메모리 안 가짜 연결 한쪽 끝. 한 프로세스 안에서 호스트·게스트를 이어 테스트한다.
    /// <see cref="Drop"/>으로 양쪽을 끊어 연결 끊김을 흉내 낸다.
    /// </summary>
    public sealed class LoopbackTransport : ITransport
    {
        readonly Queue<string> _inbox = new Queue<string>();
        LoopbackTransport _peer;
        LinkState _state = LinkState.Connected;

        public LinkState State => _state;
        /// <summary>보낸 메시지 수·글자 수 (테스트용).</summary>
        public int SentCount { get; private set; }
        public long SentChars { get; private set; }

        LoopbackTransport() { }

        public static void CreatePair(out LoopbackTransport a, out LoopbackTransport b)
        {
            a = new LoopbackTransport();
            b = new LoopbackTransport();
            a._peer = b;
            b._peer = a;
        }

        public void Send(string message)
        {
            if (_state != LinkState.Connected || _peer._state != LinkState.Connected)
                return;
            SentCount++;
            SentChars += message.Length;
            _peer._inbox.Enqueue(message);
        }

        public bool TryReceive(out string message)
        {
            if (_inbox.Count > 0)
            {
                message = _inbox.Dequeue();
                return true;
            }
            message = null;
            return false;
        }

        public void Close() => Drop();

        /// <summary>양쪽 모두 끊는다. 이미 받은 메시지는 남는다.</summary>
        public void Drop()
        {
            _state = LinkState.Closed;
            if (_peer != null)
                _peer._state = LinkState.Closed;
        }
    }

    /// <summary>가짜 방. <see cref="Connect"/>가 게스트 쪽 끝을 주고, 호스트는 TryAccept로 반대쪽을 받는다.</summary>
    public sealed class LoopbackListener : ITransportListener
    {
        readonly Queue<LoopbackTransport> _pending = new Queue<LoopbackTransport>();
        bool _closed;

        public LoopbackTransport Connect()
        {
            LoopbackTransport.CreatePair(out var hostEnd, out var guestEnd);
            if (_closed)
                hostEnd.Drop();
            else
                _pending.Enqueue(hostEnd);
            return guestEnd;
        }

        public bool TryAccept(out ITransport transport)
        {
            if (_pending.Count > 0)
            {
                transport = _pending.Dequeue();
                return true;
            }
            transport = null;
            return false;
        }

        public void Close() => _closed = true;
    }
}
