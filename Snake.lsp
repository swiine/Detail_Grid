(vl-load-com)

(setq *SNK:BlockFolder* "A:\\Civil\\AutoCAD\\Global\\Australia\\NSW\\_default\\Scripts\\Snake\\_Support\\")

(setq *SNK:Frames*
  '(("A1L" "TTW_stdCountry_Block_AnnoFrame_A1L" "TTW_FrameBlock A1L.dwg" "TTW_stdCountry_Frame_A1")
    ("A1V" "TTW_stdCountry_Block_AnnoFrame_A1V" "TTW_FrameBlock A1V.dwg" "TTW_stdCountry_Frame_A1")
   )
)

(setq *SNK:Overlap* 50.0)

(setq *SNK:ScalePrefix* "ttw_scale_1:")
(setq *SNK:ScaleSuffix* "m")

(if (not (assoc *SNK:LastSize* *SNK:Frames*)) (setq *SNK:LastSize* "A1L"))
(if (not *SNK:LastScale*) (setq *SNK:LastScale* 50.0))

(defun SNK:num->str (n / s)
  (setq s (rtos n 2 4))
  (if (vl-string-search "." s)
    (setq s (vl-string-right-trim "." (vl-string-right-trim "0" s)))
  )
  s
)

(defun SNK:scale-name (n)
  (strcat *SNK:ScalePrefix* (SNK:num->str n) *SNK:ScaleSuffix*)
)

(defun SNK:drawing-scales (/ dict name lst pre suf)
  (setq pre (strcase *SNK:ScalePrefix*)
        suf (strcase *SNK:ScaleSuffix*)
  )
  (if (setq dict (dictsearch (namedobjdict) "ACAD_SCALELIST"))
    (foreach pair dict
      (if (= (car pair) 350)
        (progn
          (setq name (strcase (cdr (assoc 300 (entget (cdr pair))))))
          (if (wcmatch name (strcat pre "*" suf))
            (setq lst (cons (distof (substr name (1+ (strlen pre))
                                            (- (strlen name) (strlen pre) (strlen suf))))
                            lst))
          )
        )
      )
    )
  )
  (vl-sort (vl-remove nil lst) '<)
)

(defun SNK:ensure-scale (n)
  (if (not (member n (SNK:drawing-scales)))
    (progn
      (command "_.-SCALELISTEDIT" "_A" (SNK:scale-name n)
               (strcat "1:" (SNK:num->str (/ n 1000.0))) "_E")
      (princ (strcat "\nAdded scale " (SNK:scale-name n) " to the drawing."))
    )
  )
  (member n (SNK:drawing-scales))
)

(defun SNK:get-size (/ k)
  (initget (apply 'strcat (mapcar '(lambda (f) (strcat (car f) " ")) *SNK:Frames*)))
  (setq k (getkword (strcat "\nFrame ["
                            (apply 'strcat (cdr (apply 'append
                              (mapcar '(lambda (f) (list "/" (car f))) *SNK:Frames*))))
                            "] <" *SNK:LastSize* ">: ")))
  (setq *SNK:LastSize* (cond (k) (*SNK:LastSize*)))
)

(defun SNK:get-scale (/ avail str n pos)
  (setq avail (SNK:drawing-scales))
  (if avail
    (princ (strcat "\nScales in drawing: 1:"
                   (apply 'strcat (cdr (apply 'append
                     (mapcar '(lambda (x) (list ", 1:" (SNK:num->str x))) avail))))))
    (princ "\nNo ttw_scale scales found in this drawing; the one you enter will be added.")
  )
  (while (not n)
    (setq str (vl-string-trim " " (getstring (strcat "\nScale 1:<" (SNK:num->str *SNK:LastScale*) ">: "))))
    (cond
      ((= str "") (setq n *SNK:LastScale*))
      (t
       (if (setq pos (vl-string-search ":" str)) (setq str (substr str (+ pos 2))))
       (setq n (distof str 2))
       (cond
         ((not (and n (> n 0))) (setq n nil) (princ "\nEnter a scale like 50 or 1:50."))
         ((and avail (not (member n avail)))
          (initget "Yes No")
          (if (/= (getkword (strcat "\n" (SNK:scale-name n)
                                    " is not in the drawing. Add it? [Yes/No] <Yes>: "))
                  "No")
            n
            (setq n nil)
          )
         )
       )
      )
    )
  )
  (setq *SNK:LastScale* n)
)

(defun SNK:extents (obj / mn mx)
  (vla-getboundingbox obj 'mn 'mx)
  (list (vlax-safearray->list mn) (vlax-safearray->list mx))
)

(defun SNK:midpoint (ext)
  (mapcar '(lambda (a b) (/ (+ a b) 2.0)) (car ext) (cadr ext))
)

(defun SNK:is-polyline (ent)
  (member (cdr (assoc 0 (entget ent))) '("LWPOLYLINE" "POLYLINE"))
)

(defun SNK:get-polyline (/ ent sel last)
  (while (not ent)
    (setvar "ERRNO" 0)
    (initget "Draw")
    (setq sel (entsel "\nSelect polyline for frame or [Draw] <Draw>: "))
    (if (and (null sel) (= (getvar "ERRNO") 52)) (setq sel "Draw"))
    (cond
      ((= sel "Draw")
       (setq last (entlast))
       (princ "\nDraw the polyline (Enter to finish, C to close):")
       (setvar "CMDECHO" 1)
       (command "_.PLINE")
       (while (> (logand (getvar "CMDACTIVE") 1) 0) (command pause))
       (setvar "CMDECHO" 0)
       (if (and (entlast) (not (equal (entlast) last)) (SNK:is-polyline (entlast)))
         (setq ent (entlast))
         (progn (princ "\nNo polyline was drawn.") (setq ent 'cancel))
       )
      )
      ((null sel) (princ "\nMissed, try again."))
      ((SNK:is-polyline (car sel)) (setq ent (car sel)))
      (t (princ "\nThat is not a polyline."))
    )
  )
  (if (/= ent 'cancel) ent)
)

(defun SNK:active-space (doc)
  (if (and (= (getvar "TILEMODE") 0) (= (getvar "CVPORT") 1))
    (vla-get-paperspace doc)
    (vla-get-modelspace doc)
  )
)

(defun SNK:find-dwg (file / folder hits)
  (setq folder (if *SNK:BlockFolder*
                 (strcat (vl-string-right-trim "\\/" *SNK:BlockFolder*) "\\")))
  (cond
    ((and folder (findfile (strcat folder file))))
    ((and folder
          (setq hits (vl-directory-files folder
                       (strcat "*" (vl-string-translate "_ " "??" (vl-filename-base file)) "*.dwg") 1)))
     (findfile (strcat folder (car hits))))
    ((findfile file))
  )
)

(defun SNK:ensure-block (doc frame / path tmp wrapper)
  (if (not (tblsearch "BLOCK" (cadr frame)))
    (if (setq path (SNK:find-dwg (caddr frame)))
      (progn
        (setq tmp (vla-insertblock (vla-get-modelspace doc)
                    (vlax-3d-point '(0 0 0)) path 1 1 1 0)
              wrapper (vla-get-name tmp)
        )
        (vla-delete tmp)
        (vl-catch-all-apply 'vla-delete
          (list (vla-item (vla-get-blocks doc) wrapper)))
      )
      (princ (strcat "\nCannot find " (caddr frame) " in "
                     (if *SNK:BlockFolder* *SNK:BlockFolder* "the support paths")
                     (if (and *SNK:BlockFolder*
                              (not (vl-file-directory-p
                                     (vl-string-right-trim "\\/" *SNK:BlockFolder*))))
                       " (folder not found)."
                       ".")))
    )
  )
  (tblsearch "BLOCK" (cadr frame))
)

(defun SNK:scale-factor (n / dict name e res)
  (setq name (strcase (SNK:scale-name n)))
  (if (setq dict (dictsearch (namedobjdict) "ACAD_SCALELIST"))
    (foreach pair dict
      (if (and (= (car pair) 350)
               (= (strcase (cdr (assoc 300 (setq e (entget (cdr pair)))))) name))
        (setq res (/ (cdr (assoc 141 e)) (cdr (assoc 140 e))))
      )
    )
  )
  (cond (res) ((/ n 1000.0)))
)

(defun SNK:block-local-extents (doc name / x0 y0 x1 y1 ext)
  (vlax-for ent (vla-item (vla-get-blocks doc) name)
    (if (not (vl-catch-all-error-p
               (setq ext (vl-catch-all-apply 'SNK:extents (list ent)))))
      (setq x0 (if x0 (min x0 (car (car ext))) (car (car ext)))
            y0 (if y0 (min y0 (cadr (car ext))) (cadr (car ext)))
            x1 (if x1 (max x1 (car (cadr ext))) (car (cadr ext)))
            y1 (if y1 (max y1 (cadr (cadr ext))) (cadr (cadr ext))))
    )
  )
  (if x0 (list (list x0 y0) (list x1 y1)))
)

(defun SNK:dist2d (a b)
  (distance (list (car a) (cadr a)) (list (car b) (cadr b)))
)

(defun SNK:pt-at (pl s / len)
  (setq len (vlax-curve-getDistAtParam pl (vlax-curve-getEndParam pl)))
  (if (>= s len)
    (vlax-curve-getEndPoint pl)
    (vlax-curve-getPointAtDist pl (max s 0.0))
  )
)

(defun SNK:find-at (pl s0 p0 r len / h s lo hi mid)
  (setq h (/ r 100.0) s s0)
  (while (and (< s len) (< (SNK:dist2d p0 (SNK:pt-at pl s)) r))
    (setq s (+ s h))
  )
  (if (>= (SNK:dist2d p0 (SNK:pt-at pl (min s len))) r)
    (progn
      (setq lo (max s0 (- s h)) hi (min s len))
      (repeat 40
        (setq mid (/ (+ lo hi) 2.0))
        (if (< (SNK:dist2d p0 (SNK:pt-at pl mid)) r) (setq lo mid) (setq hi mid))
      )
      hi
    )
  )
)

(defun SNK:insert-frame (frame ins ang / last blk)
  (setq last (entlast))
  (command "_.-INSERT" (cadr frame) "_S" 1 "_R" 0 (trans ins 0 1))
  (while (> (logand (getvar "CMDACTIVE") 1) 0) (command ""))
  (if (not (equal (entlast) last))
    (progn
      (setq blk (vlax-ename->vla-object (entlast)))
      (vla-put-rotation blk ang)
      (vla-put-insertionpoint blk (vlax-3d-point ins))
      (if (and (cadddr frame) (tblsearch "LAYER" (cadddr frame)))
        (vla-put-layer blk (cadddr frame)))
      blk
    )
  )
)

(defun SNK:lx (p o a)
  (+ (* (- (car p) (car o)) (cos a)) (* (- (cadr p) (cadr o)) (sin a)))
)

(defun SNK:corner (o a x y)
  (polar (polar o a x) (+ a (/ pi 2)) y)
)

(defun SNK:min-overlap (pa aa pb ab w h / hh)
  (setq hh (/ h 2.0))
  (min (- w (SNK:lx (SNK:corner pb ab 0.0 hh) pa aa))
       (- w (SNK:lx (SNK:corner pb ab 0.0 (- hh)) pa aa))
       (SNK:lx (SNK:corner pa aa w hh) pb ab)
       (SNK:lx (SNK:corner pa aa w (- hh)) pb ab))
)

(defun SNK:frame-at (pl s p w len prevAng / s1 p1 end)
  (if (setq s1 (SNK:find-at pl s p w len))
    (setq p1 (SNK:pt-at pl s1))
    (setq p1 (vlax-curve-getEndPoint pl) end t)
  )
  (list s p s1
        (if (> (SNK:dist2d p p1) (* w 0.001)) (angle p p1) prevAng)
        (or end (>= s1 len)))
)

(defun SNK:next-frame (pl cur w h ov len / s floor step cand)
  (setq floor (+ (car cur) (* w 0.05))
        step  (/ w 200.0)
        s     (cond ((SNK:find-at pl (car cur) (cadr cur) (- w ov) len)) ((caddr cur))))
  (setq cand (SNK:frame-at pl s (SNK:pt-at pl s) w len (cadddr cur)))
  (while (and (< (SNK:min-overlap (cadr cur) (cadddr cur) (cadr cand) (cadddr cand) w h) ov)
              (> (- s step) floor))
    (setq s    (- s step)
          cand (SNK:frame-at pl s (SNK:pt-at pl s) w len (cadddr cur)))
  )
  (if (< (SNK:min-overlap (cadr cur) (cadddr cur) (cadr cand) (cadddr cand) w h) (* ov 0.999))
    (setq *SNK:Tight* t))
  cand
)

(defun c:SNAKE (/ *error* doc size frame sc f ov pl len lext w h off
                  p0 s1 cur ins cnt oldCmd oldOsm)
  (setq doc (vla-get-activedocument (vlax-get-acad-object)))

  (defun *error* (msg)
    (if oldCmd (setvar "CMDECHO" oldCmd))
    (if oldOsm (setvar "OSMODE" oldOsm))
    (vla-endundomark doc)
    (if (not (wcmatch (strcase msg t) "*break*,*cancel*,*exit*"))
      (princ (strcat "\nError: " msg))
    )
    (princ)
  )

  (setq oldCmd (getvar "CMDECHO")
        oldOsm (getvar "OSMODE"))
  (setvar "CMDECHO" 0)
  (vla-startundomark doc)

  (setq size  (SNK:get-size)
        frame (assoc size *SNK:Frames*))
  (cond
    ((not (SNK:ensure-block doc frame)))
    ((not (setq lext (SNK:block-local-extents doc (cadr frame))))
     (princ "\nCould not measure the frame block."))
    ((not (setq sc (SNK:get-scale))))
    ((not (SNK:ensure-scale sc))
     (princ (strcat "\nCould not create scale " (SNK:scale-name sc) ".")))
    ((not (setq pl (SNK:get-polyline))))
    (t
     (setq f   (SNK:scale-factor sc)
           w   (* f (- (car (cadr lext)) (car (car lext))))
           h   (* f (- (cadr (cadr lext)) (cadr (car lext))))
           off (list (* f (car (car lext)))
                     (* f (/ (+ (cadr (car lext)) (cadr (cadr lext))) 2.0)))
           ov  (* *SNK:Overlap* f)
           len (vlax-curve-getDistAtParam pl (vlax-curve-getEndParam pl))
           cnt 0
           *SNK:Tight* nil)
     (if (>= ov w) (setq ov (* w 0.5)))

     (setvar "CANNOSCALE" (SNK:scale-name sc))
     (setvar "OSMODE" 0)

     (setq p0 (vlax-curve-getStartPoint pl))
     (if (setq s1 (SNK:find-at pl 0.0 p0 w len))
       (setq p0 (polar p0 (angle (SNK:pt-at pl s1) p0) ov))
     )
     (setq cur (SNK:frame-at pl 0.0 p0 w len nil))

     (while cur
       (if (cadddr cur)
         (progn
           (setq ins (polar (polar (cadr cur) (+ (cadddr cur) pi) (car off))
                            (- (cadddr cur) (/ pi 2)) (cadr off)))
           (if (SNK:insert-frame frame (list (car ins) (cadr ins) 0.0) (cadddr cur))
             (setq cnt (1+ cnt)))
         )
       )
       (setq cur (if (and (not (nth 4 cur)) (< cnt 1000))
                   (SNK:next-frame pl cur w h ov len)))
     )

     (setvar "OSMODE" oldOsm)
     (princ (strcat "\n" (itoa cnt) " " size " frame"
                    (if (= cnt 1) "" "s") " placed at 1:" (SNK:num->str sc)
                    " with " (SNK:num->str *SNK:Overlap*) "mm overlap."))
     (if *SNK:Tight*
       (princ "\nWarning: a bend is too tight to keep the full overlap on both sides of every frame."))
    )
  )

  (setvar "CMDECHO" oldCmd)
  (vla-endundomark doc)
  (princ)
)

(princ "\nSnake loaded. Type SNAKE to run.")
(princ)
