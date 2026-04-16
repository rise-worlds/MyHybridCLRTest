using UnityEngine;

namespace FairyGUI
{
    /// <summary>
    /// 按钮悬浮吸附代码
    /// </summary>
    public class FguiOptimize : MonoBehaviour
    {
        /// <summary>
        /// 是否优化fgui
        /// </summary>
        public bool optimize = false;

        /// <summary>
        /// fgui帧间隔
        /// </summary>
        public float interval = 1000 / 30f;

        /// <summary>
        /// 累计时间
        /// </summary>
        public float addTime = 0;

        /// <summary>
        /// 单例
        /// </summary>
        public static FguiOptimize instance { get; private set; }

        /// <summary>
        /// 组件初始化
        /// </summary>
        public void Awake()
        {
            instance = this;
        }

        /// <summary>
        /// 检查时间
        /// </summary>
        /// <returns></returns>
        public bool Check()
        {
            this.addTime += Time.deltaTime * 1000;
            if (this.optimize && this.addTime < this.interval)
            {
                return false;
            }
            this.addTime = 0;
            return true;
        }
    }
}