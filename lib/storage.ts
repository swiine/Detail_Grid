import AsyncStorage from '@react-native-async-storage/async-storage';

// AsyncStorage can throw on web when site data is blocked (private windows, sandboxed
// iframes). Fall back to memory so the app still runs for the session.
const memory = new Map<string, string>();

export const storage = {
  async getItem(key: string): Promise<string | null> {
    try {
      return (await AsyncStorage.getItem(key)) ?? memory.get(key) ?? null;
    } catch {
      return memory.get(key) ?? null;
    }
  },
  async setItem(key: string, value: string): Promise<void> {
    memory.set(key, value);
    try {
      await AsyncStorage.setItem(key, value);
    } catch {}
  },
  async removeItem(key: string): Promise<void> {
    memory.delete(key);
    try {
      await AsyncStorage.removeItem(key);
    } catch {}
  },
};

export async function getJSON<T>(key: string): Promise<T | null> {
  const raw = await storage.getItem(key);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as T;
  } catch {
    return null;
  }
}

export const setJSON = (key: string, value: unknown) => storage.setItem(key, JSON.stringify(value));
