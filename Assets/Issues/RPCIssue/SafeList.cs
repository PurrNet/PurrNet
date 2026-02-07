using System;
using System.Collections;
using System.Collections.Generic;
using PurrNet.Packing;


public class ijhihuihihihi
{
	public void Write(BitPacker stream, SafeList<int> value)
	{
		if (stream.WriteIsNull(value))
		{
			Packer<List<int>>.Write(stream, value.Purrnet_Get___list());
			Packer<List<SafeList<int>.QueueItem>>.Write(stream, value.Purrnet_Get___queue());
		}
	}

	public void Read(BitPacker stream, ref SafeList<int> value)
	{
		if (stream.ReadIsNull(ref value))
		{
			List<int> value2 = default(List<int>);
			Packer<List<int>>.Read(stream, ref value2);
			value.Purrnet_Set___list(value2);
			List<SafeList<int>.QueueItem> value3 = default(List<SafeList<int>.QueueItem>);
			Packer<List<SafeList<int>.QueueItem>>.Read(stream, ref value3);
			value.Purrnet_Set___queue(value3);
		}
	}
}


public class SafeList<T> : IEnumerable<T>
{
    private List<T> _list = new();
    private List<QueueItem> _queue = new();

    public void Add(T item)
    {
        _queue.Add(new QueueItem(item, Instruction.Add));
    }

    public void Remove(T item)
    {
        _queue.Add(new QueueItem(item, Instruction.Remove));
    }

    public void UpdateList()
    {
        foreach (var item in _queue)
        {
            switch (item.instruction)
            {
                case Instruction.Add:
                    _list.Add(item.item);
                    break;
                case Instruction.Remove:
                    _list.Remove(item.item);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        _queue.Clear();
    }

    public readonly struct QueueItem : IEquatable<QueueItem>
    {
        public readonly T item;
        public readonly Instruction instruction;

        public bool Equals(QueueItem other) => EqualityComparer<T>.Default.Equals(item, other.item) && instruction == other.instruction;
        public override bool Equals(object obj) => obj is QueueItem other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(item, (int)instruction);

        public QueueItem(T item, Instruction instruction)
        {
            this.item = item;
            this.instruction = instruction;
        }
    }

    public enum Instruction
    {
        Add,
        Remove
    }

    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();


    public List<T> Purrnet_Get___list()
    {
	    return _list;
    }

    public List<QueueItem> Purrnet_Get___queue()
    {
	    return _queue;
    }

    public void Purrnet_Set___list(List<T> value)
    {
	    _list = value;
    }

    public void Purrnet_Set___queue(List<QueueItem> value)
    {
	    _queue = value;
    }
}
