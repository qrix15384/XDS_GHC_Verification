import { useEffect, useRef, useState } from "react";

// Fallback only, for the unlikely case the video's native dimensions aren't
// available yet when capture() runs — the vendor's documented floor.
const FALLBACK_CAPTURE_WIDTH = 640;
const FALLBACK_CAPTURE_HEIGHT = 480;
const MAX_IMAGE_BYTES = 1 * 1024 * 1024; // matches SelfieVerificationRequest.MaxImageBytes on the backend

// PNG is lossless (no quality/compression knob like JPEG, and NIA requires
// PNG specifically), so the only lever for file size is resolution. Start at
// the camera's native resolution and only shrink if the encoded PNG is
// actually too big, stopping well above NIA's 500x500px face-region minimum
// so a busy/detailed shot can never shrink back into the original bug.
const MIN_CAPTURE_DIMENSION = 600;
const DOWNSCALE_FACTOR = 0.85;
const MAX_CAPTURE_ATTEMPTS = 6;

function encodeCanvasToPng(canvas: HTMLCanvasElement): Promise<Blob | null> {
  return new Promise((resolve) => canvas.toBlob(resolve, "image/png"));
}

interface CameraCaptureProps {
  /** Called with the raw (no data: prefix) Base64 PNG once a capture passes the size check. */
  onCapture: (base64Png: string, sizeBytes: number) => void;
}

function describeCameraError(err: unknown): string {
  if (err instanceof DOMException) {
    switch (err.name) {
      case "NotAllowedError":
      case "PermissionDeniedError":
        return "Camera access was denied. Allow the camera permission for this site in your browser's address-bar/site settings, then try again.";
      case "NotFoundError":
      case "DevicesNotFoundError":
        return "No camera was found on this device.";
      case "NotReadableError":
      case "TrackStartError":
        return "The camera is already in use by another application or browser tab. Close it and try again.";
      case "OverconstrainedError":
        return "This camera doesn't support the requested resolution.";
    }
  }
  return err instanceof Error ? err.message : "Could not access the camera.";
}

/**
 * Live-camera-only image capture — deliberately has no file-upload <input>
 * anywhere, so there is no way to submit an existing PNG file through this
 * component. Captures at the video stream's native resolution (NIA requires
 * the face region to be at least 500x500px, which a 640x480 image can never
 * contain) — the 1MB size cap below is the safety net for oversized shots.
 */
export function CameraCapture({ onCapture }: CameraCaptureProps) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [captured, setCaptured] = useState<{ dataUrl: string; sizeBytes: number } | null>(null);
  const [starting, setStarting] = useState(false);

  useEffect(() => {
    return () => {
      streamRef.current?.getTracks().forEach((track) => track.stop());
    };
  }, []);

  async function startCamera() {
    setError(null);
    setStarting(true);
    try {
      if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) {
        throw new Error(
          `Camera access requires HTTPS or http://localhost — this page is loaded from ` +
            `${window.location.protocol}//${window.location.hostname}, which the browser treats as insecure.`,
        );
      }
      const stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: "user", width: { ideal: 1280 }, height: { ideal: 720 } },
        audio: false,
      });
      streamRef.current = stream;
      if (videoRef.current) {
        videoRef.current.srcObject = stream;
      }
    } catch (err) {
      console.error("Camera access failed:", err);
      setError(describeCameraError(err));
    } finally {
      setStarting(false);
    }
  }

  function stopCamera() {
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    if (videoRef.current) {
      videoRef.current.srcObject = null;
    }
  }

  async function capture() {
    const video = videoRef.current;
    if (!video) return;

    let width = video.videoWidth || FALLBACK_CAPTURE_WIDTH;
    let height = video.videoHeight || FALLBACK_CAPTURE_HEIGHT;
    let blob: Blob | null = null;

    for (let attempt = 0; attempt < MAX_CAPTURE_ATTEMPTS; attempt++) {
      const canvas = document.createElement("canvas");
      canvas.width = width;
      canvas.height = height;
      const ctx = canvas.getContext("2d");
      if (!ctx) return;
      ctx.drawImage(video, 0, 0, width, height);

      blob = await encodeCanvasToPng(canvas);
      if (!blob) {
        setError("Capture failed — try again.");
        return;
      }
      if (blob.size <= MAX_IMAGE_BYTES) break;
      if (Math.min(width, height) <= MIN_CAPTURE_DIMENSION) break;

      width = Math.round(width * DOWNSCALE_FACTOR);
      height = Math.round(height * DOWNSCALE_FACTOR);
    }

    if (!blob) {
      setError("Capture failed — try again.");
      return;
    }
    if (blob.size > MAX_IMAGE_BYTES) {
      setError(
        `Captured image is ${(blob.size / 1024).toFixed(0)}KB, which is over the 1MB limit even at reduced resolution. Try retaking in flatter, more even lighting.`,
      );
      return;
    }

    const finalBlob = blob;
    const reader = new FileReader();
    reader.onload = () => {
      const dataUrl = reader.result as string;
      setCaptured({ dataUrl, sizeBytes: finalBlob.size });
      setError(null);
    };
    reader.readAsDataURL(finalBlob);
  }

  function retake() {
    setCaptured(null);
  }

  function confirm() {
    if (!captured) return;
    const base64 = captured.dataUrl.slice(captured.dataUrl.indexOf(",") + 1);
    onCapture(base64, captured.sizeBytes);
  }

  return (
    <div className="camera-capture">
      {!captured && (
        <>
          <video ref={videoRef} autoPlay playsInline muted className="camera-preview" />
          <div className="camera-controls">
            {!streamRef.current && (
              <button type="button" onClick={startCamera} disabled={starting}>
                {starting ? "Starting camera…" : "Start camera"}
              </button>
            )}
            {streamRef.current && (
              <>
                <button type="button" onClick={capture}>
                  Capture photo
                </button>
                <button type="button" onClick={stopCamera}>
                  Stop camera
                </button>
              </>
            )}
          </div>
        </>
      )}

      {captured && (
        <>
          <img src={captured.dataUrl} alt="Captured selfie" className="camera-preview" />
          <p className="hint">{(captured.sizeBytes / 1024).toFixed(0)}KB (limit 1024KB)</p>
          <div className="camera-controls">
            <button type="button" onClick={retake}>
              Retake
            </button>
            <button type="button" onClick={confirm}>
              Use this photo
            </button>
          </div>
        </>
      )}

      {error && <p className="error">{error}</p>}
    </div>
  );
}
