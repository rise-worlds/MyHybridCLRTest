using System.Collections.Generic;
using FairyGUI.Utils;

namespace FairyGUI
{
    /// <summary>
    /// Gear is a connection between object and controller.
    /// </summary>
    public class GearFontSize : GearBase
    {
        Dictionary<string, int> _storage;
        int _default;

        public GearFontSize(GObject owner)
            : base(owner)
        {
        }

        protected override void Init()
        {
            _default = 20;
            if (_owner is GTextField obj)
            { 
                _default = obj.textFormat.size;
            }
            else if (_owner is GButton obj1)
            {
                _default = obj1.GetTextField().textFormat.size;
            }
            else if (_owner is GLabel obj3)
            {
                _default = obj3.GetTextField().textFormat.size;
            }  
            else if (_owner is GComboBox obj2)
            {
                _default = obj2.GetTextField().textFormat.size;
            }   
            _storage = new Dictionary<string, int>();
        }

        override protected void AddStatus(string pageId, ByteBuffer buffer)
        {
            if (pageId == null)
                _default = buffer.ReadInt();
            else
                _storage[pageId] = buffer.ReadInt();
        }

        override public void Apply()
        {
            _owner._gearLocked = true;

            int cv;
            if (!_storage.TryGetValue(_controller.selectedPageId, out cv))
                cv = _default;

 

           if (_owner is GTextField)
            { 
                TextFormat tf = ((GTextField)_owner).textFormat;
                tf.size = cv;
                ((GTextField)_owner).textFormat = tf;
            }
            else if (_owner is GButton )
            { 
                ((GButton)_owner).titleFontSize = cv;
            }
            else if (_owner is GLabel )
            { 
                ((GLabel)_owner).titleFontSize = cv;
            }  
            else if (_owner is GComboBox )
            { 
                ((GComboBox)_owner).titleFontSize = cv;            
            } 


            _owner._gearLocked = false;
        }

        override public void UpdateState()
        {
            int curSize = 0;
            if (_owner is GTextField obj)
            { 
                curSize = obj.textFormat.size;
            }
            else if (_owner is GButton obj1)
            {
                curSize = obj1.GetTextField().textFormat.size;
            }
            else if (_owner is GLabel obj3)
            {
                curSize = obj3.GetTextField().textFormat.size;
            }  
            else if (_owner is GComboBox obj2)
            {
                curSize = obj2.GetTextField().textFormat.size;
            }   
            _storage[_controller.selectedPageId] = curSize;
        }
    }
}
