1. A UI request through the gateway passes with its v2 proof.
2. Negative, from inside the network to hr-core-web with a valid user token: no proof 403; an HMAC v1 proof 403; a v2 proof made for another token 403.
3. A v2 proof with its own token passes once (200), and the same proof again is 403.