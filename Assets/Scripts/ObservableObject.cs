using System;
using System.Collections.Generic;

namespace RiseClient
{
    public class ObservableObject<T>
    {
        private event Action<T> _callAction;
        private event Action<T> _updateAction;
        private event Action<T> _onUpdateAction;
        private List<Action<T>> _onceList = new List<Action<T>>();

        private T _def;
        public T def
        {
            set { _def = value; }
        }

        private T _val;
        public T value
        {
            set
            {
                _val = value;
                if (_callAction != null)
                {
                    _callAction(value);
                }
                if (_updateAction != null)
                {
                    _updateAction(value);
                }
                if (_onUpdateAction != null)
                {
                    _onUpdateAction(value);
                }
            }
            get { return _val; }
        }

        public T rawValue
        {
            set { _val = value; }
        }

        public ObservableObject(T val)
        {
            _def = val;
            _val = val;
        }

        public void ResetValue()
        {
            _val = _def;
        }

        public void ReportChanged()
        {
            if (_callAction != null)
            {
                _callAction(_val);
            }
            if (_updateAction != null)
            {
                _updateAction(_val);
            }
            if (_onUpdateAction != null)
            {
                _onUpdateAction(_val);
            }
        }

        public void OnceUpdate(Action<T> updateFunc)
        {
            _onceList.Add(updateFunc);
            _onUpdateAction = ((T val) =>
            {
                foreach (var func in _onceList)
                {
                    func?.Invoke(val);
                }
                _onUpdateAction = null;
                _onceList.Clear();
            });
        }

        public Action Update(Action<T> updateFunc)
        {
            _updateAction += updateFunc;
            return (() =>
            {
                _updateAction -= updateFunc;
            });
        }

        public Action GetAndUpdate(Action<T> updateFunc)
        {
            updateFunc(_val);
            _updateAction += updateFunc;
            return (() =>
            {
                _updateAction -= updateFunc;
            });
        }
        public Action GetRemoveAndUpdate(Action<T> updateFunc)
        {
            updateFunc(_val);
            _updateAction -= updateFunc;
            _updateAction += updateFunc;
            return (() =>
            {
                _updateAction -= updateFunc;
            });
        }

        public void RemoveUpdate(Action<T> updateFunc)
        {
            _updateAction -= updateFunc;
        }

        public void ResetUpdate()
        {
            _updateAction = null;
        }

        public T GetAndCall(Action<T> callFunc)
        {
            callFunc(_val);
            _callAction = callFunc;
            return _val;
        }

        public void ResetCall()
        {
            _callAction = null;
        }

        public virtual void Clear()
        {
            _callAction = null;
            _updateAction = null;
            _onUpdateAction = null;
        }
    }
}