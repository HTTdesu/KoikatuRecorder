using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using System.Threading;
using System;
using System.IO;
using System.Collections.Concurrent;
using UnityEngine.Experimental.Rendering;

public class Encoder
{
    private static Encoder s_Encoder;
    public static Encoder GetInstance()
    {
        if (s_Encoder == null)
        {
            s_Encoder = new Encoder();
        }
        return s_Encoder;
    }

    public enum EncodeMode
    {
        Full,
        Balance,
        Minimum,
        Custom
    }

    private enum EncoderTaskType
    {
        Work,
        Kill
    }
    private struct EncoderTask
    {
        public EncoderTaskType Type;
        public byte[] Data;
        public GraphicsFormat DataFormat;
        public int Width;
        public int Height;
        public FileFormat Format;
        public string SavePath;
    }

    private int m_ThreadLimit = 0;
    private int m_ThreadCount = 0;
    private ConcurrentQueue<EncoderTask> m_Tasks;
    public void Init(int threadLimit, EncodeMode mode = EncodeMode.Full)
    {
        if (m_Tasks == null)
        {
            m_Tasks = new ConcurrentQueue<EncoderTask>();
        }

        switch (mode)
        {
            case EncodeMode.Full:
                m_ThreadCount = Environment.ProcessorCount - 2;
                break;
            case EncodeMode.Balance:
                m_ThreadCount = Environment.ProcessorCount / 2;
                break;
            case EncodeMode.Minimum:
                m_ThreadCount = 1;
                break;
            case EncodeMode.Custom:
                m_ThreadCount = threadLimit;
                break;
        }
        m_ThreadLimit = Mathf.Min(threadLimit, m_ThreadCount);
        m_ThreadCount = Mathf.Clamp(m_ThreadCount, 1, threadLimit);
        BUFFER_SIZE = Mathf.Max(32, m_ThreadLimit);

        for (int i = 0; i < m_ThreadCount; i++)
        {
            Thread t = new Thread(EncoderWorker);
            t.Start();
        }
    }

    public enum FileFormat
    {
        JPG,
        PNG,
        TGA,
        EXR
    }

    private int m_DecreaseCounter = 0;
    private int m_IncreaseCounter = 0;
    const int BALANCE_THRESHOLD = 16;
    int BUFFER_SIZE = 32;
    public void Encode(byte[] data, int width, int height, string path, GraphicsFormat dataFormat = GraphicsFormat.R8G8B8_SRGB, FileFormat format = FileFormat.PNG)
    {
        EncoderTask task = new EncoderTask
        {
            Type = EncoderTaskType.Work,
            Data = data,
            DataFormat = dataFormat,
            Width = width,
            Height = height,
            SavePath = path,
            Format = format
        };

        // workload balacne
        if (m_Tasks.Count == 0)
        {
            m_IncreaseCounter = 0;
            m_DecreaseCounter++;
            if (m_DecreaseCounter > BALANCE_THRESHOLD)
            {
                m_Tasks.Enqueue(new EncoderTask()
                {
                    Type = EncoderTaskType.Kill
                });
                m_ThreadCount--;
                m_DecreaseCounter = 0;
            }
        }
        else
        {
            m_DecreaseCounter = 0;
            while (m_Tasks.Count >= BUFFER_SIZE)
            {
                Thread.Sleep(10);

                m_IncreaseCounter++;
                if (m_IncreaseCounter > BALANCE_THRESHOLD)
                {
                    if (m_ThreadCount < m_ThreadLimit)
                    {
                        Thread t = new Thread(EncoderWorker);
                        t.Start();
                        m_ThreadCount++;
                    }
                    m_IncreaseCounter = 0;
                }
            }
        }
        m_Tasks.Enqueue(task);
    }

    public void EndRecord()
    {
        while (m_ThreadCount > 0)
        {
            m_Tasks.Enqueue(new EncoderTask()
            {
                Type = EncoderTaskType.Kill
            });
            m_ThreadCount--;
        }

        while (m_Tasks.Count > 0)
        {
            Thread.Sleep(100);
        }
    }

    private void EncoderWorker()
    {
        EncoderTask task;
        while (true)
        {
            if (!m_Tasks.TryDequeue(out task))
            {
                Thread.Sleep(100);
                continue;
            }

            if (task.Type == EncoderTaskType.Kill)
            {
                Debug.Log("Thread end");
                return;
            }

            byte[] file = EncodeToFormat(task.Data, task.Width, task.Height, task.DataFormat, task.Format);
            File.WriteAllBytes(task.SavePath, file);
        }
    }
    private byte[] EncodeToFormat(byte[] data, int width, int height, GraphicsFormat dataFormat, FileFormat format = FileFormat.PNG)
    {
        switch (format)
        {
            case FileFormat.JPG:
                return ImageConversion.EncodeArrayToJPG(data, dataFormat, (uint)width, (uint)height);
            case FileFormat.PNG:
                return ImageConversion.EncodeArrayToPNG(data, dataFormat, (uint)width, (uint)height);
            case FileFormat.TGA:
                return ImageConversion.EncodeArrayToTGA(data, dataFormat, (uint)width, (uint)height);
            case FileFormat.EXR:
                return ImageConversion.EncodeArrayToEXR(data, dataFormat, (uint)width, (uint)height);
            default:
                return new byte[0];
        }
    }
}
