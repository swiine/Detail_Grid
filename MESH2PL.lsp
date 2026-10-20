;;; MESH2PL.lsp
;;; ---------------------------------------------------------------------------
;;; Converts polygon meshes, polyface meshes, 3D faces and MESH objects into
;;; 3D polylines - one polyline per face.
;;;
;;; Usage:
;;;   (load "MESH2PL.lsp")
;;;   MESH2PL
;;;
;;; Options:
;;;   Edges  All     - every face becomes a closed 3D polyline (default).
;;;          Visible - invisible edges are skipped, so a face may become one
;;;                    or more open 3D polylines (or nothing if fully hidden).
;;;   Delete Yes/No  - erase the source objects after conversion.
;;;
;;; The new polylines inherit layer, linetype, color, lineweight and
;;; linetype scale from their source object.
;;; ---------------------------------------------------------------------------

(vl-load-com)

(defun m2p:dxf (code ed) (cdr (assoc code ed)))

(defun m2p:same (a b) (equal a b 1e-8))

;; Remove consecutive duplicate points (degenerate edges, e.g. the repeated
;; fourth corner of a triangular 3D face).
(defun m2p:clean (pts closed / out)
  (foreach p pts
    (if (not (and out (m2p:same p (car out))))
      (setq out (cons p out))
    )
  )
  (if (and closed (cdr out) (m2p:same (car out) (last out)))
    (setq out (cdr out))
  )
  (reverse out)
)

;; Display properties copied from the source entity onto the new polyline.
(defun m2p:props (ed / r)
  (foreach c '(8 6 62 420 370 48)
    (if (assoc c ed) (setq r (cons (assoc c ed) r)))
  )
  (reverse r)
)

;; Create a 3D polyline through PTS. Returns T if one was created.
(defun m2p:make3dpoly (pts closed props / lay)
  (setq pts (m2p:clean pts closed)
        lay (assoc 8 props)
  )
  (if (>= (length pts) 2)
    (progn
      (setq closed (and closed (> (length pts) 2)))
      (entmake
        (append '((0 . "POLYLINE")) props
                (list '(66 . 1) '(10 0.0 0.0 0.0) (cons 70 (if closed 9 8)))
        )
      )
      (foreach p pts
        (entmake (list '(0 . "VERTEX") lay (cons 10 p) '(70 . 32)))
      )
      (entmake (list '(0 . "SEQEND") lay))
      T
    )
  )
)

;; Split a face outline into polyline chains.
;; PTS - corner points in order; edge i runs from point i to point i+1 (wrapping).
;; VIS - per-edge visibility flags, or nil to treat every edge as visible.
;; Returns a list of (closed . points).
(defun m2p:chains (pts vis / n k i rp rv cur out)
  (setq n (length pts))
  (cond
    ((or (null vis) (not (member nil vis))) (list (cons T pts)))
    ((not (vl-remove nil vis)) nil)
    (T
     ;; Rotate so the outline starts on a visible edge that follows a hidden one.
     (setq k 0)
     (while (not (and (nth k vis) (not (nth (rem (+ k n -1) n) vis))))
       (setq k (1+ k))
     )
     (setq i 0)
     (repeat n
       (setq rp (cons (nth (rem (+ i k) n) pts) rp)
             rv (cons (nth (rem (+ i k) n) vis) rv)
             i  (1+ i)
       )
     )
     (setq rp  (reverse rp)
           rv  (reverse rv)
           cur (list (car rp))
           i   0
     )
     (repeat n
       (if (nth i rv)
         (setq cur (cons (nth (rem (1+ i) n) rp) cur))
         (progn
           (if (cdr cur) (setq out (cons (cons nil (reverse cur)) out)))
           (setq cur (list (nth (rem (1+ i) n) rp)))
         )
       )
       (setq i (1+ i))
     )
     (if (cdr cur) (setq out (cons (cons nil (reverse cur)) out)))
     (reverse out)
    )
  )
)

;; --- Face extraction: each returns a list of (points visibility) -----------

;; 3DFACE: bits 1/2/4/8 of group 70 mark edges 1-4 as invisible.
(defun m2p:3dface (ed / f)
  (setq f (cond ((m2p:dxf 70 ed)) (0)))
  (list
    (list
      (list (m2p:dxf 10 ed) (m2p:dxf 11 ed) (m2p:dxf 12 ed) (m2p:dxf 13 ed))
      (list (zerop (logand f 1)) (zerop (logand f 2))
            (zerop (logand f 4)) (zerop (logand f 8)))
    )
  )
)

;; Vertex entity data lists following a POLYLINE header, up to SEQEND.
(defun m2p:vertices (en / ed out)
  (while (and (setq en (entnext en))
              (= "VERTEX" (m2p:dxf 0 (setq ed (entget en)))))
    (setq out (cons ed out))
  )
  (reverse out)
)

;; Polygon mesh (POLYLINE with flag 16): an M x N grid of vertices.
;; Uses the smoothed surface vertices when the mesh has been smoothed.
(defun m2p:polygonmesh (en ed / flg m n pts i j i2 j2 imax jmax out)
  (setq flg (m2p:dxf 70 ed)
        m   (m2p:dxf 71 ed)
        n   (m2p:dxf 72 ed)
        pts (mapcar '(lambda (v) (m2p:dxf 10 v))
                    (vl-remove-if '(lambda (v) (= 16 (logand 16 (m2p:dxf 70 v))))
                                  (m2p:vertices en)))
  )
  (if (and (/= (length pts) (* m n))
           (m2p:dxf 73 ed) (m2p:dxf 74 ed)
           (= (length pts) (* (m2p:dxf 73 ed) (m2p:dxf 74 ed))))
    (setq m (m2p:dxf 73 ed)
          n (m2p:dxf 74 ed))
  )
  (if (= (length pts) (* m n))
    (progn
      (setq imax (if (= 1 (logand flg 1)) m (1- m))
            jmax (if (= 32 (logand flg 32)) n (1- n))
            i    0
      )
      (while (< i imax)
        (setq i2 (rem (1+ i) m)
              j  0)
        (while (< j jmax)
          (setq j2  (rem (1+ j) n)
                out (cons (list (list (nth (+ (* i n) j) pts)
                                      (nth (+ (* i n) j2) pts)
                                      (nth (+ (* i2 n) j2) pts)
                                      (nth (+ (* i2 n) j) pts))
                                nil)
                          out)
                j   (1+ j))
        )
        (setq i (1+ i))
      )
      (reverse out)
    )
    (progn (princ "\nSkipped a polygon mesh with an unexpected vertex count.") nil)
  )
)

;; Polyface mesh (POLYLINE with flag 64): coordinate vertices (flags 64+128)
;; followed by face records (flag 128) whose groups 71-74 are 1-based vertex
;; indices; a negative index marks the edge starting at that vertex invisible.
(defun m2p:polyfacemesh (en / verts fl pts vis out idx)
  (foreach v (m2p:vertices en)
    (setq fl (m2p:dxf 70 v))
    (cond
      ((= 192 (logand fl 192)) (setq verts (cons (m2p:dxf 10 v) verts)))
      ((= 128 (logand fl 192))
       (setq pts nil vis nil)
       (foreach c '(71 72 73 74)
         (if (and (setq idx (m2p:dxf c v)) (/= 0 idx))
           (setq pts (cons (abs idx) pts)
                 vis (cons (> idx 0) vis))
         )
       )
       (if pts (setq out (cons (list (reverse pts) (reverse vis)) out)))
      )
    )
  )
  ;; Resolve indices once all coordinate vertices are known.
  (setq verts (reverse verts))
  (vl-remove nil
    (mapcar '(lambda (f / p)
               (setq p (mapcar '(lambda (i) (nth (1- i) verts)) (car f)))
               (if (not (member nil p)) (list p (cadr f))))
            (reverse out))
  )
)

;; MESH (subdivision mesh): explode a copy into 3D faces and read those.
(defun m2p:subdmesh (en / res out)
  (setq res (vl-catch-all-apply 'vla-Explode (list (vlax-ename->vla-object en))))
  (if (vl-catch-all-error-p res)
    (progn (princ "\nCould not explode a MESH object.") nil)
    (progn
      (foreach o (vlax-safearray->list (vlax-variant-value res))
        (if (= "AcDbFace" (vla-get-ObjectName o))
          (setq out (append out (m2p:3dface (entget (vlax-vla-object->ename o)))))
        )
        (vla-Delete o)
      )
      out
    )
  )
)

(defun m2p:faces (en / ed typ flg)
  (setq ed  (entget en)
        typ (m2p:dxf 0 ed))
  (cond
    ((= typ "3DFACE") (m2p:3dface ed))
    ((= typ "MESH") (m2p:subdmesh en))
    ((= typ "POLYLINE")
     (setq flg (m2p:dxf 70 ed))
     (cond
       ((= 64 (logand flg 64)) (m2p:polyfacemesh en))
       ((= 16 (logand flg 16)) (m2p:polygonmesh en ed))
     )
    )
  )
)

;; --- Command ---------------------------------------------------------------

(defun c:MESH2PL (/ *error* doc ss mode del i en props faces cnt src)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))
  (defun *error* (msg)
    (vla-EndUndoMark doc)
    (if (not (wcmatch (strcase msg) "*CANCEL*,*QUIT*,*EXIT*"))
      (princ (strcat "\nError: " msg))
    )
    (princ)
  )
  (princ "\nSelect polygon meshes, polyface meshes, 3D faces or meshes: ")
  (if (setq ss (ssget '((-4 . "<OR")
                          (0 . "3DFACE")
                          (0 . "MESH")
                          (-4 . "<AND") (0 . "POLYLINE") (-4 . "&") (70 . 80) (-4 . "AND>")
                        (-4 . "OR>"))))
    (progn
      (initget "All Visible")
      (setq mode (cond ((getkword "\nEdges to trace [All/Visible] <All>: ")) ("All")))
      (initget "Yes No")
      (setq del (cond ((getkword "\nDelete source objects? [Yes/No] <No>: ")) ("No")))
      (vla-StartUndoMark doc)
      (setq i 0 cnt 0 src 0)
      (repeat (sslength ss)
        (setq en    (ssname ss i)
              props (m2p:props (entget en))
              faces (m2p:faces en)
              i     (1+ i))
        (if faces (setq src (1+ src)))
        (foreach f faces
          (foreach ch (m2p:chains (car f) (if (= mode "Visible") (cadr f)))
            (if (m2p:make3dpoly (cdr ch) (car ch) props) (setq cnt (1+ cnt)))
          )
        )
        (if (and faces (= del "Yes"))
          (vl-catch-all-apply 'vla-Delete (list (vlax-ename->vla-object en)))
        )
      )
      (vla-EndUndoMark doc)
      (princ (strcat "\nCreated " (itoa cnt) " polyline(s) from "
                     (itoa src) " object(s)."))
    )
  )
  (princ)
)

(princ "\nMESH2PL loaded. Type MESH2PL to convert meshes and 3D faces to polylines.")
(princ)
