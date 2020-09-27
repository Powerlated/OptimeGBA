namespace OptimeGBAEmulator {
    public abstract class SaveProvider {
        public abstract byte Read8(uint addr);
        public abstract void Write8(uint addr, byte val);
    }
}