import { useEffect, useRef, useState } from "react";

const CAPTURE_WIDTH = 640;
const CAPTURE_HEIGHT = 480;
const MAX_IMAGE_BYTES = 1 * 1024 * 1024; // matches SelfieVerificationRequest.MaxImageBytes on the backend

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
 * component. Captures at a fixed 640x480 (the vendor's documented floor)
 * rather than the stream's native resolution, since that reliably stays
 * well under the 1MB cap.
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

  function capture() {
    const video = videoRef.current;
    if (!video) return;

    const canvas = document.createElement("canvas");
    canvas.width = CAPTURE_WIDTH;
    canvas.height = CAPTURE_HEIGHT;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    ctx.drawImage(video, 0, 0, CAPTURE_WIDTH, CAPTURE_HEIGHT);

    canvas.toBlob((blob) => {
      if (!blob) {
        setError("Capture failed — try again.");
        return;
      }
      if (blob.size > MAX_IMAGE_BYTES) {
        setError(
          `Captured image is ${(blob.size / 1024).toFixed(0)}KB, which is over the 1MB limit. Try retaking in better/flatter lighting.`,
        );
        return;
      }

      const reader = new FileReader();
      reader.onload = () => {
        const dataUrl = reader.result as string;
        setCaptured({ dataUrl, sizeBytes: blob.size });
        setError(null);
      };
      reader.readAsDataURL(blob);
    }, "image/png");
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
