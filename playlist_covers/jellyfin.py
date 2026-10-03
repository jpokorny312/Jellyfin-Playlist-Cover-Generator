"""Minimal Jellyfin API client – just what the cover generator needs."""
from __future__ import annotations

import base64
import io
from typing import Optional

import requests
from PIL import Image


class Jellyfin:
    def __init__(self, base_url: str, api_key: str, user_id: str, timeout: int = 30):
        self.base = base_url.rstrip("/")
        self.user_id = user_id
        self.timeout = timeout
        self.s = requests.Session()
        self.s.headers["Authorization"] = f'MediaBrowser Token="{api_key}"'

    def _get(self, path: str, **params):
        r = self.s.get(f"{self.base}{path}", params=params, timeout=self.timeout)
        r.raise_for_status()
        return r

    def playlists(self) -> list[dict]:
        r = self._get(
            "/Items",
            userId=self.user_id,
            includeItemTypes="Playlist",
            recursive="true",
            fields="ChildCount",
        )
        return r.json()["Items"]

    def playlist_items(self, playlist_id: str) -> list[dict]:
        r = self._get(
            f"/Playlists/{playlist_id}/Items",
            userId=self.user_id,
            fields="ProductionYear",
        )
        return r.json()["Items"]

    def image(self, item_id: str, kind: str, max_width: int) -> Optional[Image.Image]:
        """Fetch Backdrop/Logo of an item; None if the item has none."""
        r = self.s.get(
            f"{self.base}/Items/{item_id}/Images/{kind}",
            params={"maxWidth": max_width},
            timeout=self.timeout,
        )
        if r.status_code == 404:
            return None
        r.raise_for_status()
        return Image.open(io.BytesIO(r.content))

    def upload_primary(self, playlist_id: str, jpeg: bytes) -> None:
        r = self.s.post(
            f"{self.base}/Items/{playlist_id}/Images/Primary",
            data=base64.b64encode(jpeg),
            headers={"Content-Type": "image/jpeg"},
            timeout=self.timeout,
        )
        r.raise_for_status()
