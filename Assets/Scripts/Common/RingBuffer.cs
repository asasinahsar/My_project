using UnityEngine;

namespace UnityVirtual.Common
{
    public class RingBuffer<T>
    {
        private struct BufferItem
        {
            public T item;
            public float timestamp;
        }

        private BufferItem[] buffer;
        private int headIndex;
        private int currentCount;
        private int capacity;

        // コンストラクタ：容量と、要素を事前生成するためのファクトリ関数を受け取る
        public RingBuffer(int capacity, System.Func<T> factory)
        {
            this.capacity = capacity;
            buffer = new BufferItem[capacity];
            
            for (int i = 0; i < capacity; i++)
            {
                buffer[i] = new BufferItem
                {
                    item = factory(),
                    timestamp = 0f
                };
            }
            
            headIndex = 0;
            currentCount = 0;
        }

        // 次に書き込むべき（事前確保済みの）要素への参照を返す
        // ※呼び出し側はこれを受け取り、値を直接書き換えた後に Commit() を呼ぶ
        public T GetNextWritableItem()
        {
            return buffer[headIndex].item;
        }

        // 書き込みが完了した要素をタイムスタンプと共に確定し、インデックスを進める
        public void Commit(float timestamp)
        {
            buffer[headIndex].timestamp = timestamp;
            headIndex = (headIndex + 1) % capacity;
            if (currentCount < capacity) currentCount++;
        }

        // 指定ミリ秒前のフレームを返す（GCを防ぐため、一番近いフレームを返す設計）
        public T GetAtDelay(float delayMs)
        {
            if (currentCount == 0) return buffer[0].item;

            float targetTime = Time.realtimeSinceStartup - (delayMs / 1000f);

            // 最新のフレームから過去へ遡る
            T closestItem = buffer[(headIndex - 1 + capacity) % capacity].item;

            for (int i = 1; i <= currentCount; i++)
            {
                int index = (headIndex - i + capacity) % capacity;
                if (buffer[index].timestamp <= targetTime)
                {
                    closestItem = buffer[index].item;
                    break;
                }
            }

            return closestItem;
        }
    }
}