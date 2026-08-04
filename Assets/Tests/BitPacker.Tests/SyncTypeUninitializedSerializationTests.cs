using System;
using System.Runtime.Serialization;
using NUnit.Framework;
using PurrNet;

public class SyncTypeUninitializedSerializationTests
{
    static T CreateUninitialized<T>()
    {
        return (T)FormatterServices.GetUninitializedObject(typeof(T));
    }

    static void AssertCallbacksSurvive<T>(Action<T> afterDeserialize, Action<T> beforeSerialize)
    {
        var instance = CreateUninitialized<T>();

        Assert.DoesNotThrow(() => afterDeserialize(instance),
            $"{typeof(T)}.OnAfterDeserialize threw on an instance built without field initializers.");
        Assert.DoesNotThrow(() => beforeSerialize(instance),
            $"{typeof(T)}.OnBeforeSerialize threw on an instance built without field initializers.");
    }

    [Test]
    public void SyncListSurvivesUninitializedInstance()
    {
        AssertCallbacksSurvive<SyncList<int>>(x => x.OnAfterDeserialize(), x => x.OnBeforeSerialize());
    }

    [Test]
    public void SyncDictionarySurvivesUninitializedInstance()
    {
        AssertCallbacksSurvive<SyncDictionary<int, string>>(x => x.OnAfterDeserialize(), x => x.OnBeforeSerialize());
    }

    [Test]
    public void SyncArraySurvivesUninitializedInstance()
    {
        AssertCallbacksSurvive<SyncArray<int>>(x => x.OnAfterDeserialize(), x => x.OnBeforeSerialize());
    }

    [Test]
    public void SyncQueueSurvivesUninitializedInstance()
    {
        AssertCallbacksSurvive<SyncQueue<int>>(x => x.OnAfterDeserialize(), x => x.OnBeforeSerialize());
    }

    [Test]
    public void SerializableDictionaryDetectsSerializabilityWithoutConstructor()
    {
        var serialized = CreateUninitialized<SerializableDictionary<int, string>>();

        Assert.That(serialized.IsSerializable, Is.True);
    }
}
