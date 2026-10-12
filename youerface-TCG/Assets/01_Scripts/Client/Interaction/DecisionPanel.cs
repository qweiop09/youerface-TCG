using System.Collections.Generic;
using TCG.Client.Battle;
using TCG.Client.Common;
using TCG.Core.Commands;
using TCG.Core.State;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TCG.Client.Interaction
{
    /// <summary>
    /// 체인 선택 창 (GameDesign §7.2). 내 효과가 동시에 발동했을 때 체인에 올릴 순서를 고르고,
    /// 선택 효과는 빼고 확정할 수 있다. 대응 기회(ChainResponse)에는 "패스" 버튼만 있다 (대응 블록은 나중에).
    /// </summary>
    public sealed class DecisionPanel : MonoBehaviour
    {
        sealed class Row
        {
            public PendingTrigger Trigger;
            public Button Button;
            public TextMeshProUGUI Label;
        }

        BattleSession _session;
        GameObject _root;
        TextMeshProUGUI _title, _subtitle;
        Button _confirm, _defaultOrder;
        Transform _list;
        readonly List<Row> _rows = new List<Row>();
        readonly List<int> _order = new List<int>();
        PendingDecision _shownFor;
        /// <summary>선택 효과 1개뿐이면 순서 대신 "발동 / 발동 안 함"만 묻는다.</summary>
        bool _yesNo;

        public bool IsOpen => _root != null && _root.activeSelf;
        public IReadOnlyList<int> CurrentOrder => _order;

        public void Init(BattleSession session, Transform canvas)
        {
            _session = session;
            var panel = UiFactory.Panel(canvas, "DecisionPanel", new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(980, 640),
                new Color(0.08f, 0.09f, 0.12f, 0.94f));
            _root = panel.gameObject;
            _title = UiFactory.Text(panel.transform, "Title", "", new Vector2(0.5f, 1f), new Vector2(0, -50), new Vector2(900, 60), 40);
            _subtitle = UiFactory.Text(panel.transform, "Subtitle", "", new Vector2(0.5f, 1f), new Vector2(0, -105), new Vector2(900, 50), 24);
            _subtitle.color = new Color(1f, 1f, 1f, 0.7f);
            _list = UiFactory.Rect(panel.transform, "List", new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(900, 380));
            _defaultOrder = UiFactory.Button(panel.transform, "Default", "제시 순서대로", new Vector2(0.5f, 0f), new Vector2(-170, 70),
                new Vector2(300, 76), new Color(0.3f, 0.34f, 0.42f), ChooseDefault, 28f);
            _confirm = UiFactory.Button(panel.transform, "Confirm", "확정", new Vector2(0.5f, 0f), new Vector2(170, 70),
                new Vector2(300, 76), new Color(0.75f, 0.55f, 0.2f), Confirm, 30f);
            _root.SetActive(false);
        }

        void Update()
        {
            if (_session == null)
                return;
            var decision = _session.HumanHasDecision ? _session.State.Decision : null;
            if (decision == null)
            {
                if (_root.activeSelf)
                    _root.SetActive(false);
                _shownFor = null;
                return;
            }
            if (decision != _shownFor)
                Open(decision);
        }

        void Open(PendingDecision decision)
        {
            _shownFor = decision;
            _order.Clear();
            foreach (var row in _rows)
                Destroy(row.Button.gameObject);
            _rows.Clear();
            _root.SetActive(true);

            if (decision.Type == DecisionType.ChainResponse)
            {
                _title.text = "체인에 대응하시겠습니까?";
                _subtitle.text = "대응할 수 있는 효과가 없으면 자동으로 넘어갑니다.";
                UiFactory.SetLabel(_confirm, "패스");
                _defaultOrder.gameObject.SetActive(false);
                _confirm.interactable = true;
                return;
            }

            _defaultOrder.gameObject.SetActive(true);
            var only = decision.Options.Count == 1 ? _session.State.PendingTriggers.Find(t => t.Id == decision.Options[0]) : null;
            _yesNo = only != null && only.IsOptional;
            if (_yesNo)
            {
                var source = _session.State.GetEntity(only.SourceId);
                var effect = Core.Effects.EffectLookup.Get(source, only.EffectIndex, _session.Engine.Registry);
                _title.text = "효과를 발동하시겠습니까?";
                _subtitle.text = $"{source?.Definition.Name} — {EffectText.Describe(effect)}";
                UiFactory.SetLabel(_defaultOrder, "발동 안 함");
                UiFactory.SetLabel(_confirm, "발동");
                _confirm.interactable = true;
                _order.Add(only.Id);
                return;
            }

            _title.text = "효과를 체인에 올릴 순서를 고르세요";
            _subtitle.text = "고른 순서대로 체인 1, 2… (마지막 번호부터 처리). [선택] 효과는 빼도 됩니다.";
            UiFactory.SetLabel(_defaultOrder, "제시 순서대로");
            UiFactory.SetLabel(_confirm, "확정");

            for (int i = 0; i < decision.Options.Count; i++)
            {
                var trigger = _session.State.PendingTriggers.Find(t => t.Id == decision.Options[i]);
                if (trigger == null)
                    continue;
                var row = new Row { Trigger = trigger };
                row.Button = UiFactory.Button(_list, $"Option {trigger.Id}", "", new Vector2(0.5f, 1f), new Vector2(0, -40 - i * 86),
                    new Vector2(880, 74), new Color(0.2f, 0.23f, 0.3f), () => Toggle(row), 26f);
                row.Label = row.Button.GetComponentInChildren<TextMeshProUGUI>();
                _rows.Add(row);
            }
            Redraw();
        }

        void Toggle(Row row)
        {
            int id = row.Trigger.Id;
            if (_order.Contains(id))
                _order.Remove(id);
            else
                _order.Add(id);
            Redraw();
        }

        void Redraw()
        {
            foreach (var row in _rows)
            {
                var source = _session.State.GetEntity(row.Trigger.SourceId);
                var effect = Core.Effects.EffectLookup.Get(source, row.Trigger.EffectIndex, _session.Engine.Registry);
                int position = _order.IndexOf(row.Trigger.Id);
                string slot = position >= 0 ? $"<color=#FFD24D>체인 {position + 1}</color>   " : "            ";
                string kind = row.Trigger.IsOptional ? "[선택]" : "[강제]";
                row.Label.text = $"{slot}{kind} {source?.Definition.Name} — {EffectText.Describe(effect)}";
                row.Button.image.color = position >= 0 ? new Color(0.32f, 0.36f, 0.24f) : new Color(0.2f, 0.23f, 0.3f);
            }
            _confirm.interactable = AllMandatoryChosen();
        }

        bool AllMandatoryChosen()
        {
            foreach (var row in _rows)
            {
                if (!row.Trigger.IsOptional && !_order.Contains(row.Trigger.Id))
                    return false;
            }
            return true;
        }

        /// <summary>제시된 순서대로 모두 고른다. "발동 / 발동 안 함" 창에서는 "발동 안 함" 버튼.</summary>
        public void ChooseDefault()
        {
            if (_yesNo)
            {
                _order.Clear();
                Confirm();
                return;
            }
            _order.Clear();
            foreach (var row in _rows)
                _order.Add(row.Trigger.Id);
            Redraw();
        }

        public void Confirm()
        {
            var decision = _session.State.Decision;
            if (decision == null)
                return;
            Command command = decision.Type == DecisionType.ChainResponse
                ? new PassChainCommand(_session.Human)
                : (Command)new ChooseTriggersCommand(_session.Human, new List<int>(_order));
            if (_session.TrySubmit(command, out var reason))
                _root.SetActive(false);
            else
                _subtitle.text = reason;
        }
    }
}
