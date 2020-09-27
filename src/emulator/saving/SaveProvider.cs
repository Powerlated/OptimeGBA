namespace OptimeGBA {
    public interface SaveProvider {
        byte Read8(uint addr);
        void Write8(uint addr, byte val);
    }
}