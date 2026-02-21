namespace PurrNet.Packing
{
    public interface IDuplicate<out T>
    {
        T Duplicate();
    }
}
