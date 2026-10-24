import { act } from '@testing-library/react';

// État réseau du navigateur dans jsdom (navigator.onLine) : coupé ou rétabli, avec l'événement correspondant.
let online = true;
Object.defineProperty(window.navigator, 'onLine', { configurable: true, get: () => online });

export function setNetwork(next: boolean) {
  act(() => {
    online = next;
    window.dispatchEvent(new Event(next ? 'online' : 'offline'));
  });
}
