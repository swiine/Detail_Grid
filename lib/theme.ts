export interface Palette {
  p: string; // primary
  pm: string; // primary mid (buttons)
  pd: string; // primary deep
  pl: string; // primary tinted surface
  p2: string; // lighter accent
  p3: string; // lightest accent (titles)
  border: string;
  border2: string;
  border3: string;
}

const ACCENTS: Record<string, Palette> = {
  violet: {
    p: '#9B59FF', pm: '#7B3FEF', pd: '#5A2DB8', pl: '#1E103A', p2: '#C084FC', p3: '#E0AAFF',
    border: 'rgba(155,89,255,0.18)', border2: 'rgba(155,89,255,0.35)', border3: 'rgba(155,89,255,0.6)',
  },
  crimson: {
    p: '#FF3B5C', pm: '#E0213F', pd: '#9E1028', pl: '#33101A', p2: '#FF7A8E', p3: '#FFB3BF',
    border: 'rgba(255,59,92,0.18)', border2: 'rgba(255,59,92,0.35)', border3: 'rgba(255,59,92,0.6)',
  },
  azure: {
    p: '#3FA9FF', pm: '#1F86E0', pd: '#135C9E', pl: '#0C2236', p2: '#7FC8FF', p3: '#BFE4FF',
    border: 'rgba(63,169,255,0.18)', border2: 'rgba(63,169,255,0.35)', border3: 'rgba(63,169,255,0.6)',
  },
  emerald: {
    p: '#00D68F', pm: '#00AD73', pd: '#007A51', pl: '#0A2A20', p2: '#5CF0BC', p3: '#B0FFE2',
    border: 'rgba(0,214,143,0.18)', border2: 'rgba(0,214,143,0.35)', border3: 'rgba(0,214,143,0.6)',
  },
  gold: {
    p: '#FFB800', pm: '#E09A00', pd: '#9E6C00', pl: '#2E2208', p2: '#FFD25C', p3: '#FFE9AD',
    border: 'rgba(255,184,0,0.18)', border2: 'rgba(255,184,0,0.35)', border3: 'rgba(255,184,0,0.6)',
  },
};

export const palette = (theme: string): Palette => ACCENTS[theme] ?? ACCENTS.violet;

export const C = {
  bg: '#08080F',
  bg2: '#0E0E1A',
  card: '#141422',
  card2: '#1A1A2E',
  text: '#EAE8FF',
  text2: '#9B96CC',
  text3: '#5A5580',
  gold: '#FFB800',
  teal: '#00E5A0',
  cyan: '#00F5FF',
  pink: '#FF4EF0',
  blue: '#4A9BF0',
  danger: '#FF3355',
};

export const F = {
  title: 'Cinzel_700Bold',
  titleBlack: 'Cinzel_900Black',
  label: 'Rajdhani_700Bold',
  labelSemi: 'Rajdhani_600SemiBold',
  body: 'Exo2_400Regular',
  bodySemi: 'Exo2_600SemiBold',
};
